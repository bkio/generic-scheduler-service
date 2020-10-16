/// MIT License, Copyright Burak Kara, burak@burak.io, https://en.wikipedia.org/wiki/MIT_License

using System;
using System.IO;
using System.Net;
using BCloudServiceUtilities;
using BCommonUtilities;
using BWebServiceUtilities;
using ServiceUtilities.All;
using Newtonsoft.Json.Linq;
using SchedulerService.Endpoints.Structures;

namespace SchedulerService.Endpoints
{
    internal class UnscheduleRequest : InternalWebServiceBase
    {
        private readonly IBDatabaseServiceInterface DatabaseService;

        public UnscheduleRequest(string _InternalPrivateKey, IBDatabaseServiceInterface _DatabaseService) : base(_InternalPrivateKey)
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
            //GET is supported for easy calls from terraform scripts since it only has GET request support out of the box.
            //https://www.terraform.io/docs/providers/http/data_source.html
            //POST calls are recommended to use over GET.

            if (_Context.Request.HttpMethod != "DELETE")
            {
                _ErrorMessageAction?.Invoke("ScheduleRequest: DELETE methods are accepted. But received request method:  " + _Context.Request.HttpMethod);
                return BWebResponse.MethodNotAllowed("DELETE methods are accepted. But received request method: " + _Context.Request.HttpMethod);
            }

            JObject ParsedBody = null;

            if (_Context.Request.HttpMethod == "DELETE")
            {
                using (var InputStream = _Context.Request.InputStream)
                {
                    using (var ResponseReader = new StreamReader(InputStream))
                    {
                        try
                        {
                            ParsedBody = JObject.Parse(ResponseReader.ReadToEnd());
                        }
                        catch (Exception e)
                        {
                            _ErrorMessageAction?.Invoke("Read request body stage has failed. Exception: " + e.Message + ", Trace: " + e.StackTrace);
                            return BWebResponse.BadRequest("Malformed request body. Request must be a valid json form.");
                        }
                    }
                }
            }

            if (!ParsedBody.ContainsKey(ScheduledUrlTaskDBEntry.URL_PROPERTY) ||
                ParsedBody[ScheduledUrlTaskDBEntry.URL_PROPERTY].Type != JTokenType.String)
            {
                return BWebResponse.BadRequest("Request must contain all necessary fields validly. Given argument: " + ParsedBody.ToString());
            }

            string Url = (string)ParsedBody[ScheduledUrlTaskDBEntry.URL_PROPERTY];

            if (!Uri.TryCreate(Url, UriKind.Absolute, out Uri UriResult)
                || (UriResult.Scheme != Uri.UriSchemeHttp && UriResult.Scheme != Uri.UriSchemeHttps))
            {
                return BWebResponse.BadRequest("Given field " + ScheduledUrlTaskDBEntry.URL_PROPERTY + " is invalid. Given argument: " + ParsedBody.ToString());
            }

            if (!DatabaseService.DeleteItem(
                ScheduledUrlTaskDBEntry.DBSERVICE_SCHEDULED_URL_TASKS_TABLE(),
                ScheduledUrlTaskDBEntry.KEY_NAME_TASK_URL,
                new BPrimitiveType(WebUtility.UrlEncode(Url)),
                out JObject _,
                EBReturnItemBehaviour.DoNotReturn,
                _ErrorMessageAction))
            {
                return BWebResponse.InternalError("Database delete operation has failed.");
            }

            return BWebResponse.StatusOK("Task has been unscheduled.");
        }
    }
}
