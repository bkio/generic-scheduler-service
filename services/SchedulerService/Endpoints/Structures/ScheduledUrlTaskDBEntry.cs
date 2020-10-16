/// MIT License, Copyright Burak Kara, burak@burak.io, https://en.wikipedia.org/wiki/MIT_License

using ServiceUtilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SchedulerService.Endpoints.Structures
{
    //DB Table entry
    //KeyName = KEY_NAME_TASK_URL
    public class ScheduledUrlTaskDBEntry
    {
        public static string DBSERVICE_SCHEDULED_URL_TASKS_TABLE() { return "scheduled-url-tasks-" + Resources_DeploymentManager.Get().GetDeploymentBranchNameEscapedLoweredWithDash(); }

        public const string KEY_NAME_TASK_URL = "urlEncoded";

        public const string URL_PROPERTY = "url";
        public const string VERB_PROPERTY = "verb";
        public const string HEADERS_PROPERTY = "headers";
        public const string BODY_PROPERTY = "body";
        public const string RETRY_COUNT_PROPERTY = "retryCount";
        public const string RETRIED_COUNT_PROPERTY = "retriedCount";
        public const string CANCEL_RETRY_ON_SUCCESS_PROPERTY = "cancelRetryOnSuccess";
        public const string CANCEL_ON_RETURN_CODES_PROPERTY = "cancelOnReturnCodes";
        public const string RETRY_IN_SECONDS_PROPERTY = "retryInSeconds";
        public const string SCHEDULED_TO_TIME_PROPERTY = "scheduledToTime";

        //All fields
        public static readonly string[] Properties =
        {
            URL_PROPERTY,
            VERB_PROPERTY,
            HEADERS_PROPERTY,
            BODY_PROPERTY,
            RETRY_COUNT_PROPERTY,
            RETRIED_COUNT_PROPERTY,
            CANCEL_RETRY_ON_SUCCESS_PROPERTY,
            CANCEL_ON_RETURN_CODES_PROPERTY,
            RETRY_IN_SECONDS_PROPERTY,
            SCHEDULED_TO_TIME_PROPERTY
        };

        [JsonProperty(URL_PROPERTY)]
        public string Url = "";

        [JsonProperty(VERB_PROPERTY)]
        public string Verb = "";

        [JsonProperty(HEADERS_PROPERTY)]
        public JObject Headers = new JObject();

        [JsonProperty(BODY_PROPERTY)]
        public JObject Body = new JObject();

        [JsonProperty(RETRY_COUNT_PROPERTY)]
        public int RetryCount = -1;

        [JsonProperty(RETRIED_COUNT_PROPERTY)]
        public int RetriedCount = 0;

        [JsonProperty(CANCEL_RETRY_ON_SUCCESS_PROPERTY)]
        public bool bCancelRetryOnSuccess = true;

        [JsonProperty(CANCEL_ON_RETURN_CODES_PROPERTY)]
        public JArray CancelOnReturnCodes = new JArray();

        [JsonProperty(RETRY_IN_SECONDS_PROPERTY)]
        public int RetryInSeconds = 60;

        [JsonProperty(SCHEDULED_TO_TIME_PROPERTY)]
        public long ScheduledToTime = 0;
    }
}