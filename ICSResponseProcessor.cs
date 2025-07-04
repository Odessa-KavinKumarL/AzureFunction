using FunctionApp;
using Microsoft.Azure.Functions.Worker.Http;
using System;
using System.IO;
using System.Linq;
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
            var submitICSResponse = xdoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "SubmitICSResponse");

            if (submitICSResponse != null)
            {
                var returnStatus = submitICSResponse.Descendants().FirstOrDefault(e => e.Name.LocalName == "ReturnStatus");

                if (returnStatus != null && returnStatus.Value == "E")
                {
                    //return "None";
                }
                var firstChild = submitICSResponse.Elements().FirstOrDefault();
                if (firstChild != null)
                {
                    string rootName = firstChild.Name.LocalName;

                    switch (rootName)
                    {
                        case "SearchDataResponse":
                            string body = await new StreamReader(req.Body).ReadToEndAsync();
                            return await searchDataResponseProcessor.ProcessLosResponse(soapXmlString, req);

                        case "ObtainDecisionResponse":
                            return await systemDecisionProcessor.ProcessObtainSystemResponse(soapXmlString, req);

                        default:
                            throw new Exception("Unknown response type inside SubmitICSResponse.");
                    }
                }
            }

            return await searchDataResponseProcessor.ProcessLosResponse(soapXmlString, req);
        }
    }
}
