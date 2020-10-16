/// MIT License, Copyright Burak Kara, burak@burak.io, https://en.wikipedia.org/wiki/MIT_License

using System;
using System.Net;
using BCloudServiceUtilities;
using BWebServiceUtilities;
using SchedulerService.Endpoints.Structures;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;
using System.Threading;
using BCommonUtilities;
using System.Collections.Concurrent;
using System.Security.Authentication;
using ServiceUtilities.All;

namespace SchedulerService.Endpoints
{
    internal class OnMinuteCallRequest : InternalWebServiceBase
    {
        private readonly IBDatabaseServiceInterface DatabaseService;

        public OnMinuteCallRequest(string _InternalPrivateKey, IBDatabaseServiceInterface _DatabaseService) : base(_InternalPrivateKey)
        {
            DatabaseService = _DatabaseService;
        }

        protected override BWebServiceResponse Process(HttpListenerContext _Context, Action<string> _ErrorMessageAction = null)
        {
            GetTracingService()?.On_FromServiceToService_Received(_Context, _ErrorMessageAction);

            var Result = OnRequest_Internal(_Context, _ErrorMessageAction);

            GetTracingService()?.On_FromServiceToService_Sent(_Context, _ErrorMessageAction);

            return Result;
        }

        private BWebServiceResponse OnRequest_Internal(HttpListenerContext _Context, Action<string> _ErrorMessageAction)
        {
            //Any verb is accepted.

            var StartTimestamp = MSSinceEpoch();

            if (!DatabaseService.ScanTable(
                ScheduledUrlTaskDBEntry.DBSERVICE_SCHEDULED_URL_TASKS_TABLE(),
                out List<JObject> URLTasks_UrlEncoded,
                _ErrorMessageAction))
            {
                return BWebResponse.StatusOK("Table does not exist or ScanTable operation has failed."); //Still ok.
            }
            if (URLTasks_UrlEncoded.Count == 0)
            {
                return BWebResponse.StatusOK("There is no task in the database.");
            }

            var URLTasks = new List<ScheduledUrlTaskDBEntry>();
            foreach (var Current in URLTasks_UrlEncoded)
            {
                URLTasks.Add(JsonConvert.DeserializeObject<ScheduledUrlTaskDBEntry>(Current.ToString()));
            }

            long RemainedMS = 50000 - (MSSinceEpoch() - StartTimestamp); //60-50=10 seconds is for possible delays.
            while (RemainedMS > 0)
            {
                var BeforeTS = MSSinceEpoch();
                SecondCheck(_Context, URLTasks, _ErrorMessageAction);
                
                var Diff = MSSinceEpoch() - BeforeTS;
                if (Diff < 10000)
                {
                    Thread.Sleep(10000 - (int)Diff);
                }
                RemainedMS -= (MSSinceEpoch() - BeforeTS);
            }

            return BWebResponse.StatusAccepted("Request has been accepted.");
        }

        private static long MSSinceEpoch()
        {
            return new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds();
        }

        private void SecondCheck(HttpListenerContext _Context, List<ScheduledUrlTaskDBEntry> _URLTasks, Action<string> _ErrorMessageAction)
        {
            var WaitUntil = new ManualResetEvent(false);
            var Counter = new ConcurrentStack<bool>();

            var DeleteItems = new ConcurrentQueue<ScheduledUrlTaskDBEntry>();
            var FailedItems = new ConcurrentQueue<ScheduledUrlTaskDBEntry>();
            var UpdateItems = new ConcurrentQueue<ScheduledUrlTaskDBEntry>();

            var TimeHasComeFor = new List<ScheduledUrlTaskDBEntry>();
            foreach (var URLTask in _URLTasks)
            {
                if (URLTask.ScheduledToTime < new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds())
                {
                    TimeHasComeFor.Add(URLTask);
                    Counter.Push(true);
                }
            }
            foreach (var Current in TimeHasComeFor)
            {
                var Process = Current;
                BTaskWrapper.Run(() =>
                {
                    var bRequestSuccess = PerformHttpRequest(out int StatusCode, Process.Url, Process.Verb, Process.Headers, Process.Body, _ErrorMessageAction);
                    
                    bool bDeleted = false;
                    foreach (int CancelReturnCode in Process.CancelOnReturnCodes)
                    {
                        if (CancelReturnCode == StatusCode)
                        {
                            bDeleted = true;
                            DeleteItems.Enqueue(Process);
                            break;
                        }
                    }

                    if (!bDeleted)
                    {
                        if (bRequestSuccess)
                        {
                            if (Process.bCancelRetryOnSuccess)
                            {
                                DeleteItems.Enqueue(Process);
                            }
                            else
                            {
                                if (Process.RetryCount == -1
                                    || (Process.RetriedCount + 1) <= Process.RetryCount)
                                {
                                    UpdateItems.Enqueue(Process);
                                }
                                else
                                {
                                    DeleteItems.Enqueue(Process);
                                }
                            }
                        }
                        else
                        {
                            FailedItems.Enqueue(Process);
                        }
                    }

                    Counter.TryPop(out bool _);
                    if (Counter.IsEmpty)
                    {
                        try
                        {
                            WaitUntil.Set();
                        }
                        catch (Exception) { }
                    }
                });
            }

            try
            {
                if (TimeHasComeFor.Count > 0)
                {
                    WaitUntil.WaitOne();
                }
                WaitUntil.Close();
            }
            catch (Exception) { }

            while (FailedItems.TryDequeue(out ScheduledUrlTaskDBEntry Failed))
            {
                if (Failed.RetryCount == -1
                    || (Failed.RetriedCount + 1) <= Failed.RetryCount)
                {
                    Failed.RetriedCount++;
                    Failed.ScheduledToTime = new DateTimeOffset(DateTime.UtcNow.AddSeconds(Failed.RetryInSeconds)).ToUnixTimeSeconds();

                    DatabaseService.UpdateItem(
                        ScheduledUrlTaskDBEntry.DBSERVICE_SCHEDULED_URL_TASKS_TABLE(),
                        ScheduledUrlTaskDBEntry.KEY_NAME_TASK_URL,
                        new BPrimitiveType(WebUtility.UrlEncode(Failed.Url)),
                        JObject.Parse(JsonConvert.SerializeObject(Failed)),
                        out JObject _,
                        EBReturnItemBehaviour.DoNotReturn,
                        null,
                        _ErrorMessageAction);
                }
                else
                {
                    DeleteItems.Enqueue(Failed);
                }
            }

            while (UpdateItems.TryDequeue(out ScheduledUrlTaskDBEntry Update))
            {
                Update.RetriedCount++;
                Update.ScheduledToTime = new DateTimeOffset(DateTime.UtcNow.AddSeconds(Update.RetryInSeconds)).ToUnixTimeSeconds();

                DatabaseService.UpdateItem(
                    ScheduledUrlTaskDBEntry.DBSERVICE_SCHEDULED_URL_TASKS_TABLE(),
                    ScheduledUrlTaskDBEntry.KEY_NAME_TASK_URL,
                    new BPrimitiveType(WebUtility.UrlEncode(Update.Url)),
                    JObject.Parse(JsonConvert.SerializeObject(Update)),
                    out JObject _,
                    EBReturnItemBehaviour.DoNotReturn,
                    null,
                    _ErrorMessageAction);
            }

            while (DeleteItems.TryDequeue(out ScheduledUrlTaskDBEntry Delete))
            {
                DatabaseService.DeleteItem(
                    ScheduledUrlTaskDBEntry.DBSERVICE_SCHEDULED_URL_TASKS_TABLE(),
                    ScheduledUrlTaskDBEntry.KEY_NAME_TASK_URL,
                    new BPrimitiveType(WebUtility.UrlEncode(Delete.Url)),
                    out JObject _,
                    EBReturnItemBehaviour.DoNotReturn,
                    _ErrorMessageAction);
            }
        }

        //Returns true if HttpResponseCode < 400
        private bool PerformHttpRequest(out int _StatusCode, string _URL, string _Verb, JObject _Headers, JObject _Body, Action<string> _ErrorMessageAction)
        {
            _StatusCode = 500;
            string HttpResponseContent = "";
            string ExceptionMessage = "";

            using var Handler = new HttpClientHandler
            {
                SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls11 | SslProtocols.Tls,
                ServerCertificateCustomValidationCallback = (a, b, c, d) => true
            };
            {
                using (var Client = new HttpClient(Handler))
                {
                    foreach (var HeaderKeyValue in _Headers)
                    {
                        Client.DefaultRequestHeaders.TryAddWithoutValidation(HeaderKeyValue.Key, HeaderKeyValue.Value.Type == JTokenType.String ? (string)HeaderKeyValue.Value : HeaderKeyValue.Value.ToString());
                    }

                    Task<HttpResponseMessage> RequestTask = null;
                    StringContent RequestContent = null;
                    try
                    {
                        switch (_Verb)
                        {
                            case "GET":
                                RequestTask = Client.GetAsync(_URL);
                                break;
                            case "DELETE":
                                RequestTask = Client.DeleteAsync(_URL);
                                break;
                            case "POST":
                                RequestContent = new StringContent(_Body.ToString(), Encoding.UTF8, "application/json");
                                RequestTask = Client.PostAsync(_URL, RequestContent);
                                break;
                            case "PUT":
                                RequestContent = new StringContent(_Body.ToString(), Encoding.UTF8, "application/json");
                                RequestTask = Client.PutAsync(_URL, RequestContent);
                                break;
                        }

                        using (RequestTask)
                        {
                            RequestTask.Wait();

                            using (var Response = RequestTask.Result)
                            {
                                _StatusCode = (int)Response.StatusCode;

                                using (var ResponseContent = Response.Content)
                                {
                                    using (var ReadResponseTask = ResponseContent.ReadAsStringAsync())
                                    {
                                        ReadResponseTask.Wait();
                                        HttpResponseContent = ReadResponseTask.Result;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        ExceptionMessage = "Url: " + _URL + ", Message: " + e.Message + ((e.InnerException != null && e.InnerException != e) ? (", Inner Exception: " + e.InnerException.Message) : "") + ", Trace: " + e.StackTrace;
                    }
                    finally
                    {
                        try { RequestTask?.Dispose(); } catch (Exception) { }
                        try { RequestContent?.Dispose(); } catch (Exception) { }
                    }
                }
            }

            if (_StatusCode < 400)
            {
                return true;
            }

            _ErrorMessageAction("Http request has failed. Response code: " + _StatusCode + ", Response: " + HttpResponseContent + ", Exception if any: " + ExceptionMessage + ", URL: " + _URL);
            return false;
        }
    }
}