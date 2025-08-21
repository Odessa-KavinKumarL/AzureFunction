using FunctionApp;
using Microsoft.Azure.Functions.Worker.Http;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace MyFunctionApp.Services
{
    public class ICSResponseProcessor
    {
        public async Task<HttpResponseData> ProcessICSResponse(string soapXmlString, HttpRequestData req)
        {
            XNamespace s = "http://schemas.xmlsoap.org/soap/envelope/";
            XNamespace ns = "http://schemas.delagelanden.com/service/ics/1.0";

            var xdoc = XDocument.Parse(soapXmlString);
            var searchDataResponseProcessor = new ParseSearchDataResponse();
            var systemDecisionProcessor = new ParseSystemDecisionResponse();
            var manualDecisionProcessor = new ParseManualDecisionResponse();

            var submitICSResponse = xdoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "SubmitICSResponse");
            HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);

            if (submitICSResponse != null)
            {
                var returnStatus = submitICSResponse.Descendants().FirstOrDefault(e => e.Name.LocalName == "ReturnStatus");

                if (returnStatus != null && returnStatus.Value == "E")
                {

                    var appIdElement = xdoc.Descendants()
                                           .FirstOrDefault(e => e.Name.LocalName == "Reference");
                    string applicationId = appIdElement?.Value ?? string.Empty;

                    var errorResponse = new
                    {
                        Input = new
                        {
                            CreditRequestNumber = applicationId,
                            ReturnStatus = "E"
                        }
                    };
                    await response.WriteAsJsonAsync(errorResponse);

                    return response;
                }
                var firstChild = submitICSResponse.Elements().FirstOrDefault();
                if (firstChild != null)
                {
                    string rootName = firstChild.Name.LocalName;

                    switch (rootName)
                    {
                        case "SearchDataResponse":
                            string body = await new StreamReader(req.Body).ReadToEndAsync();
                            response = await searchDataResponseProcessor.ProcessLosResponse(soapXmlString, req);
                            return response;

                        case "ObtainDecisionResponse":
                            response = await systemDecisionProcessor.ProcessObtainSystemResponse(soapXmlString, req);
                            return response;

                        //case "ICSManualDecisionResponse":
                        //    return await new ParseManualDecisionResponse().ProcessManualDecisionResponse(soapXmlString, req);

                        default:
                            response.StatusCode = HttpStatusCode.InternalServerError;
                            response.WriteString("Unknown response type inside SubmitICSResponse.");
                            return response;
                    }
                }
            }
            var manualDecisionResponse = xdoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "ICSManualDecisionResponse");
            if (manualDecisionResponse != null)
            {
                response = await manualDecisionProcessor.ProcessManualDecisionResponse(soapXmlString, req);
                return response;
            }

            return response;
        }
    }
}
