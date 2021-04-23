/// MIT License, Copyright Burak Kara, burak@burak.io, https://en.wikipedia.org/wiki/MIT_License

using System;
using System.Collections.Generic;
using System.Threading;
using BCloudServiceUtilities;
using BServiceUtilities;
using BWebServiceUtilities;
using ServiceUtilities;
using ServiceUtilities.All;
using SchedulerService.Endpoints;

namespace SchedulerService
{
    class Program
    {
        static void Main()
        {
            Console.WriteLine("Initializing the service...");

//#if (Debug || DEBUG)
//            if (!ServicesDebugOnlyUtilities.CalledFromMain()) return;
//#endif

            // In case of a cloud component dependency or environment variable is added/removed;
            // Relative terraform script and microservice-dependency-map.cs must be updated as well.

            /*
            * Common initialization step
            */
            if (!BServiceInitializer.Initialize(out BServiceInitializer ServInit,
                new string[][]
                {
                    //new string[] { "MONGODB_CONNECTION_STRING" },
                    new string[] { "MONGODB_CLIENT_CONFIG" },
                    new string[] { "MONGODB_PASSWORD" },
                    new string[] { "MONGODB_DATABASE" },

                    new string[] { "DEPLOYMENT_BRANCH_NAME" },
                    new string[] { "DEPLOYMENT_BUILD_NUMBER" },

                    new string[] { "INTERNAL_CALL_PRIVATE_KEY" }
                }))
                return;
            bool bInitSuccess = true;
            //bInitSuccess &= ServInit.WithTracingService();
            bInitSuccess &= ServInit.WithDatabaseService();
            if (!bInitSuccess) return;

            Resources_DeploymentManager.Get().SetDeploymentBranchNameAndBuildNumber(ServInit.RequiredEnvironmentVariables["DEPLOYMENT_BRANCH_NAME"], ServInit.RequiredEnvironmentVariables["DEPLOYMENT_BUILD_NUMBER"]);

            string InternalPrivateKey = ServInit.RequiredEnvironmentVariables["INTERNAL_CALL_PRIVATE_KEY"];

            /*
            * Web-http service initialization
            */
            var WebServiceEndpoints = new List<BWebPrefixStructure>()
            {
                new BWebPrefixStructure(new string[] { "/scheduler/internal/schedule*" }, () => new ScheduleRequest(InternalPrivateKey, ServInit.DatabaseService)),
                new BWebPrefixStructure(new string[] { "/scheduler/internal/unschedule*" }, () => new UnscheduleRequest(InternalPrivateKey, ServInit.DatabaseService)),
                new BWebPrefixStructure(new string[] { "/scheduler/internal/on_minute_call*" }, () => new OnMinuteCallRequest(InternalPrivateKey, ServInit.DatabaseService))
            };
            var BWebService = new BWebService(WebServiceEndpoints.ToArray(), ServInit.ServerPort/*, ServInit.TracingService*/);
            BWebService.Run((string Message) =>
            {
                ServInit.LoggingService.WriteLogs(BLoggingServiceMessageUtility.Single(EBLoggingServiceLogType.Info, Message), ServInit.ProgramID, "WebService");
            });

            /*
            * Make main thread sleep forever
            */
            Thread.Sleep(Timeout.Infinite);
        }
    }
}
