/*****
 *  Bing Entity Search custom skill.
 *  
 *  IMPORTANT: Make sure to update credentials in the code.
 *  
 *  After you enter your credentials, this is what a sample input looks like:
 *  
   Sample input:

   {
      "values": [
           {
               "recordId": "foobar2",
               "data":
               {
                  "name":  "Pablo Picasso"
               }
           },
           {
               "recordId": "foo1",
               "data":
               {
                  "name":  "Microsoft"
               }
           }
      ]
   }
    
  
  Sample output:
 
    {
        "values": [
            {
                "recordId": "foobar2",
                "data": {
                    "name": "Pablo Picasso",
                    "description": "Pablo Ruiz Picasso was a Spanish painter, sculptor, printmaker, ceramicist, stage designer, poet and playwright who spent most of his adult life in France. Regarded as one of the most influential artists of the 20th century, he is known for co-founding the Cubist movement, the invention of constructed sculpture, the co-invention of collage, and for the wide variety of styles that he helped develop and explore. Among his most famous works are the proto-Cubist Les Demoiselles d'Avignon, and Guernica, a dramatic portrayal of the bombing of Guernica by the German and Italian airforces during the Spanish Civil War.",
                    "imageUrl": "https://www.bing.com/th?id=AMMS_e8c719d1c081e929c60a2f112d659d96&w=110&h=110&c=12&rs=1&qlt=80&cdv=1&pid=16.2",
                    "url": "http://en.wikipedia.org/wiki/Pablo_Picasso",
                    "licenseAttribution": "Text under CC-BY-SA license",
                    "entities": { ... }
                 }
            },
            ...
        ]
    }
  
 *****************************************************/


using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;


namespace SampleSkills
{
    /// <summary>
    /// Sample custom skill that wraps the bing entity search API to connect it with a 
    /// congitive search pipeline.
    /// </summary>
    public static class BingEntitySearch
    {
        #region Credentials
        // IMPORTATNT: Make sure to enter your credential and to verify the API endpoint matches yours.
        static string bingApiEndpoint = "https://api.cognitive.microsoft.com/bing/v7.0/entities/";
        static string key = "";  
        #endregion

        #region Class used to deserialize the request
        public class InputRecord
        {
            public class InputRecordData
            {
                public string name;
            }

            public string recordId { get; set; }
            public InputRecordData data { get; set; }
        }

        private class WebApiRequest
        {
            public List<InputRecord> values { get; set; }
        }
        #endregion

        #region Classes used to serialize the response
        public class OutputRecord
        {
            public class OutputRecordData
            {
                public string name;
                public string description;
                public string imageUrl;
                public string url;
                public string licenseAttribution;
                public Entities entities { get; set; }
            }

            public class OutputRecordErrors
            {
                public string message { get; set; }
            }

            public class OutputRecordWarnings
            {
                public string message { get; set; }
            }

            public string recordId { get; set; }
            public OutputRecordData data { get; set; }
            public List<OutputRecordErrors> errors { get; set; }
            public List<OutputRecordWarnings> warnings { get; set; }
        }

        private class WebApiResponse
        {
            public List<OutputRecord> values { get; set; }
        }
        #endregion

        #region Classes used to interact with the Bing API
        public class Entities
        {
            public BingEntity[] value { get; set; }
        }

        public class BingEntity
        {
            public class Entitypresentationinfo
            {
                public string entityScenario { get; set; }
                public string[] entityTypeHints { get; set; }
                public object entityTypeDisplayHint { get; set; }
            }

            public class License
            {
                public string name { get; set; }
                public string url { get; set; }
            }

            public class Contractualrule
            {
                public string _type { get; set; }
                public string targetPropertyName { get; set; }
                public bool mustBeCloseToContent { get; set; }
                public License license { get; set; }
                public string licenseNotice { get; set; }
                public string text { get; set; }
                public string url { get; set; }
            }

            public class Provider
            {
                public string _type { get; set; }
                public string url { get; set; }
            }


            public class Image
            {
                public string name { get; set; }
                public string thumbnailUrl { get; set; }
                public Provider[] provider { get; set; }
                public string hostPageUrl { get; set; }
                public int width { get; set; }
                public int height { get; set; }
            }

            public Contractualrule[] contractualRules { get; set; }
            public Image image { get; set; }
            public string description { get; set; }
            public string bingId { get; set; }
            public string webSearchUrl { get; set; }
            public string name { get; set; }
            public string url { get; set; }
            public Entitypresentationinfo entityPresentationInfo { get; set; }
        }
        #endregion

        #region The Azure Function definition

        [FunctionName("EntitySearch")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Entity Search function: C# HTTP trigger function processed a request.");

            var response = new WebApiResponse();
            response.values = new List<OutputRecord>();

            string requestBody = new StreamReader(req.Body).ReadToEnd();
            var data = JsonConvert.DeserializeObject<WebApiRequest>(requestBody);

            // Do some schema validation
            if (data == null)
            {
                return new BadRequestObjectResult("The request schema does not match expected schema.");
            }
            if (data.values == null)
            {
                return new BadRequestObjectResult("The request schema does not match expected schema. Could not find values array.");
            }

            // Calculate the response for each value.
            foreach (var record in data.values)
            {
                if (record == null || record.recordId == null) continue;

                OutputRecord responseRecord = new OutputRecord();
                responseRecord.recordId = record.recordId;

                try
                {
                    string nameName = record.data.name;
                    responseRecord.data = GetEntityMetadata(nameName).Result;
                }
                catch (Exception e)
                {
                    // Something bad happened, log the issue.
                    var error = new OutputRecord.OutputRecordErrors
                    {
                        message = e.Message
                    };

                    responseRecord.errors = new List<OutputRecord.OutputRecordErrors>();
                    responseRecord.errors.Add(error);
                }
                finally
                {
                    response.values.Add(responseRecord);
                }
            }

            return (ActionResult)new OkObjectResult(response);
        }

        #endregion

        #region Methods to call the Bing API
        public class RetryHandler : DelegatingHandler
        {
            // Strongly consider limiting the number of retries - "retry forever" is
            // probably not the most user friendly way you could respond to "the
            // network cable got pulled out."
            private const int MaxRetries = 10;

            public RetryHandler(HttpMessageHandler innerHandler)
                : base(innerHandler)
            { }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                HttpResponseMessage response = null;
                for (int i = 0; i < MaxRetries; i++)
                {
                    response = await base.SendAsync(request, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        return response;
                    }

                    //Log.Info("Retrying " + request.RequestUri.ToString());
                    Thread.Sleep(1000);
                }

                return response;
            }
        }

        /// <summary>
        /// Helper function that replaces nulls for empty strings.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static String EmptyOrValue(String value)
        {
            if (value == null) return "";
            return value;
        }

        /// <summary>
        /// Gets metadata for a particular entity based on its name using Bing Entity Search
        /// </summary>
        /// <param name="nameName">The image to extract objects for.</param>
        /// <returns>Asynchronous task that returns objects identified in the image. </returns>
        async static Task<OutputRecord.OutputRecordData> GetEntityMetadata(string nameName)
        {
            var uri = bingApiEndpoint + "?q=" + nameName + "&mkt=en-us&count=10&offset=0&safesearch=Moderate";
            var result = new OutputRecord.OutputRecordData();

            using (var client = new HttpClient(new RetryHandler(new HttpClientHandler())))
            using (var request = new HttpRequestMessage())
            {
                request.Method = HttpMethod.Get;
                request.RequestUri = new Uri(uri);
                request.Headers.Add("Ocp-Apim-Subscription-Key", key);

                var response = await client.SendAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();

                result = JsonConvert.DeserializeObject<OutputRecord.OutputRecordData>(responseBody);

                // In addition to the list of entities that could match the name, for simplicity let's return information
                // for the top match as additional metadata at the root object.
                result = AddTopEntityMetadata(result);

                // Do some cleanup on the returned result.
                result.imageUrl = EmptyOrValue(result.imageUrl);
                result.description = EmptyOrValue(result.description);
                if (result.name == null) { result.name = EmptyOrValue(nameName); }
                result.url = EmptyOrValue(result.url);
                result.licenseAttribution = EmptyOrValue(result.licenseAttribution);
            }

            return result;
        }

        public class CoreData
        {
            public string description;
            public string name;
            public string imageUrl;
            public string url;
            public string licenseAttribution;
        }

        static OutputRecord.OutputRecordData AddTopEntityMetadata(OutputRecord.OutputRecordData rootObject)
        {

            CoreData coreData = new CoreData();

            if (rootObject.entities != null)
            {
                foreach (BingEntity entity in rootObject.entities.value)
                {
                    if (entity.entityPresentationInfo != null)
                    {
                        if (entity.entityPresentationInfo.entityTypeHints != null)
                        {
                            if (entity.entityPresentationInfo.entityTypeHints[0] != "Person" &&
                                entity.entityPresentationInfo.entityTypeHints[0] != "Organization" &&
                                entity.entityPresentationInfo.entityTypeHints[0] != "Location"
                                )
                            {
                                continue;
                            }
                        }
                    }

                    if (entity.description != null && entity.description != "")
                    {
                        rootObject.description = entity.description;
                        rootObject.name = entity.name;
                        if (entity.image != null)
                        {
                            rootObject.imageUrl = entity.image.thumbnailUrl;
                        }

                        if (entity.contractualRules != null)
                        {
                            foreach (var rule in entity.contractualRules)
                            {
                                if (rule.targetPropertyName == "description")
                                {
                                    rootObject.url = rule.url;
                                }

                                if (rule._type == "ContractualRules/LicenseAttribution")
                                {
                                    rootObject.licenseAttribution = rule.licenseNotice;
                                }
                            }
                        }

                        return rootObject;
                    }
                }
            }

            return rootObject;
        }
        #endregion
    }
}
