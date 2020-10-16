/// MIT License, Copyright Burak Kara, burak@burak.io, https://en.wikipedia.org/wiki/MIT_License

using System;
using System.IO;
using System.Net;
using BCloudServiceUtilities;
using BWebServiceUtilities;
using SchedulerService.Endpoints.Structures;
using Newtonsoft.Json.Linq;
using BCommonUtilities;
using Newtonsoft.Json;
using ServiceUtilities.All;

namespace SchedulerService.Endpoints
{
    internal class ScheduleRequest : InternalWebServiceBase
    {
        private readonly IBDatabaseServiceInterface DatabaseService;

        public ScheduleRequest(string _InternalPrivateKey, IBDatabaseServiceInterface _DatabaseService) : base(_InternalPrivateKey)
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

            if (_Context.Request.HttpMethod != "GET" && _Context.Request.HttpMethod != "POST")
            {
                _ErrorMessageAction?.Invoke("ScheduleRequest: GET and POST methods are accepted. But received request method:  " + _Context.Request.HttpMethod);
                return BWebResponse.MethodNotAllowed("GET and POST methods are accepted. But received request method: " + _Context.Request.HttpMethod);
            }

            JObject ParsedBody = null;

            if (_Context.Request.HttpMethod == "GET")
            {
                if (!UrlParameters.ContainsKey("serialized"))
                {
                    return BWebResponse.BadRequest("Malformed request. Url parameters must contain serialized= key.");
                }

                try
                {
                    ParsedBody = JObject.Parse(WebUtility.UrlDecode(UrlParameters["serialized"]));
                }
                catch (Exception e)
                {
                    _ErrorMessageAction?.Invoke("Read request body stage has failed. Exception: " + e.Message + ", Trace: " + e.StackTrace + ", Content: " + UrlParameters["serialized"]);
                    return BWebResponse.BadRequest("Malformed request body. Request must be a valid json form.");
                }
            }
            else
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
                ParsedBody[ScheduledUrlTaskDBEntry.URL_PROPERTY].Type != JTokenType.String ||
                !ParsedBody.ContainsKey(ScheduledUrlTaskDBEntry.VERB_PROPERTY) ||
                ParsedBody[ScheduledUrlTaskDBEntry.VERB_PROPERTY].Type != JTokenType.String ||
                (ParsedBody.ContainsKey(ScheduledUrlTaskDBEntry.HEADERS_PROPERTY) &&
                ParsedBody[ScheduledUrlTaskDBEntry.HEADERS_PROPERTY].Type != JTokenType.Object) ||
                (ParsedBody.ContainsKey(ScheduledUrlTaskDBEntry.BODY_PROPERTY) &&
                ParsedBody[ScheduledUrlTaskDBEntry.BODY_PROPERTY].Type != JTokenType.Object) ||
                !ParsedBody.ContainsKey(ScheduledUrlTaskDBEntry.RETRY_COUNT_PROPERTY) ||
                ParsedBody[ScheduledUrlTaskDBEntry.RETRY_COUNT_PROPERTY].Type != JTokenType.Integer ||
                (ParsedBody.ContainsKey(ScheduledUrlTaskDBEntry.CANCEL_RETRY_ON_SUCCESS_PROPERTY) &&
                ParsedBody[ScheduledUrlTaskDBEntry.CANCEL_RETRY_ON_SUCCESS_PROPERTY].Type != JTokenType.Boolean) ||
                (ParsedBody.ContainsKey(ScheduledUrlTaskDBEntry.CANCEL_ON_RETURN_CODES_PROPERTY) &&
                ParsedBody[ScheduledUrlTaskDBEntry.CANCEL_ON_RETURN_CODES_PROPERTY].Type != JTokenType.Array) ||
                !ParsedBody.ContainsKey(ScheduledUrlTaskDBEntry.RETRY_IN_SECONDS_PROPERTY) ||
                ParsedBody[ScheduledUrlTaskDBEntry.RETRY_IN_SECONDS_PROPERTY].Type != JTokenType.Integer)
            {
                return BWebResponse.BadRequest("Request must contain all necessary fields validly. Given argument: " + ParsedBody.ToString());
            }

            var Body = new JObject();
            bool bCancelRetryOnSuccess = true;
            var CancelOnReturnCodes = new JArray();
            string Url = (string)ParsedBody[ScheduledUrlTaskDBEntry.URL_PROPERTY];

            if (!Uri.TryCreate(Url, UriKind.Absolute, out Uri UriResult)
                || (UriResult.Scheme != Uri.UriSchemeHttp && UriResult.Scheme != Uri.UriSchemeHttps))
            {
                return BWebResponse.BadRequest("Given field " + ScheduledUrlTaskDBEntry.URL_PROPERTY + " is invalid. Given argument: " + ParsedBody.ToString());
            }
            string Verb = (string)ParsedBody[ScheduledUrlTaskDBEntry.VERB_PROPERTY];
            if (Verb != "GET" && Verb != "POST" && Verb != "PUT" && Verb != "DELETE")
            {
                return BWebResponse.BadRequest("Field " + ScheduledUrlTaskDBEntry.VERB_PROPERTY + " must be one of these: GET, POST, PUT, DELETE. Given argument: " + ParsedBody.ToString());
            }

            JObject Headers;
            if (ParsedBody.ContainsKey(ScheduledUrlTaskDBEntry.HEADERS_PROPERTY))
            {
                Headers = (JObject)ParsedBody[ScheduledUrlTaskDBEntry.HEADERS_PROPERTY];
            }
            else
            {
                Headers = new JObject();
            }

            if (ParsedBody.ContainsKey(ScheduledUrlTaskDBEntry.BODY_PROPERTY))
            {
                if (Verb == "GET" || Verb == "DELETE")
                {
                    return BWebResponse.BadRequest("GET/DELETE requests cannot contain field " + ScheduledUrlTaskDBEntry.BODY_PROPERTY + ", given argument: " + ParsedBody.ToString());
                }
                Body = (JObject)ParsedBody[ScheduledUrlTaskDBEntry.BODY_PROPERTY];
            }
            else if (Verb == "POST" || Verb == "PUT")
            {
                return BWebResponse.BadRequest("POST/PUT requests must contain field " + ScheduledUrlTaskDBEntry.BODY_PROPERTY + ", given argument: " + ParsedBody.ToString());
            }

            if (ParsedBody.ContainsKey(ScheduledUrlTaskDBEntry.CANCEL_RETRY_ON_SUCCESS_PROPERTY))
            {
                bCancelRetryOnSuccess = (bool)ParsedBody[ScheduledUrlTaskDBEntry.CANCEL_RETRY_ON_SUCCESS_PROPERTY];
            }

            if (ParsedBody.ContainsKey(ScheduledUrlTaskDBEntry.CANCEL_ON_RETURN_CODES_PROPERTY))
            {
                CancelOnReturnCodes = (JArray)ParsedBody[ScheduledUrlTaskDBEntry.CANCEL_ON_RETURN_CODES_PROPERTY];
                foreach (var Item in CancelOnReturnCodes)
                {
                    if (Item.Type != JTokenType.Integer)
                    {
                        return BWebResponse.BadRequest("All elements of " + ScheduledUrlTaskDBEntry.CANCEL_ON_RETURN_CODES_PROPERTY + " must be integer");
                    }
                }
            }

            int RetryCount = (int)ParsedBody[ScheduledUrlTaskDBEntry.RETRY_COUNT_PROPERTY];
            if (RetryCount < -1)
            {
                return BWebResponse.BadRequest("Field " + ScheduledUrlTaskDBEntry.RETRY_COUNT_PROPERTY + " must be greater than or equal to -1. Given argument: " + ParsedBody.ToString());
            }
            int RetryInSeconds = (int)ParsedBody[ScheduledUrlTaskDBEntry.RETRY_IN_SECONDS_PROPERTY];
            if (RetryInSeconds < 0)
            {
                return BWebResponse.BadRequest("Field " + ScheduledUrlTaskDBEntry.RETRY_IN_SECONDS_PROPERTY + " must be greater than or equal to 0. Given argument: " + ParsedBody.ToString());
            }

            if (!DatabaseService.UpdateItem(
                ScheduledUrlTaskDBEntry.DBSERVICE_SCHEDULED_URL_TASKS_TABLE(),
                ScheduledUrlTaskDBEntry.KEY_NAME_TASK_URL,
                new BPrimitiveType(WebUtility.UrlEncode(Url)),
                JObject.Parse(JsonConvert.SerializeObject(new ScheduledUrlTaskDBEntry()
                {
                    Url = Url,
                    Verb = Verb,
                    Body = Body,
                    Headers = Headers,
                    RetryCount = RetryCount,
                    bCancelRetryOnSuccess = bCancelRetryOnSuccess,
                    CancelOnReturnCodes = CancelOnReturnCodes,
                    RetryInSeconds = RetryInSeconds,
                    ScheduledToTime = new DateTimeOffset(DateTime.UtcNow.AddSeconds(RetryInSeconds)).ToUnixTimeSeconds()
                })),
                out _,
                EBReturnItemBehaviour.DoNotReturn,
                null,
                _ErrorMessageAction))
            {
                return BWebResponse.InternalError("Database write operation has failed.");
            }

            return BWebResponse.StatusOK("Task has been scheduled.");
        }
    }
}