using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using System.Text;
using Formatting = Newtonsoft.Json.Formatting;

namespace FunctionApp
{
    public class ParseSearchDataResponse
    {
        private ILogger<ParseSearchDataResponse> _logger;

        private const string SoapNamespace = "http://schemas.xmlsoap.org/soap/envelope/";
        private const string IcsNamespace = "http://schemas.delagelanden.com/service/ics/1.0";
        private const string HeaderNamespace = "http://schemas.delagelanden.com/model/soapdllheader/1.0";

        public async Task<HttpResponseData> ProcessLosResponse(string requestBody, HttpRequestData req)
        {
            try
            {
                if (!IsValidRequestBody(requestBody, out var badRequestTask, req))
                    return await badRequestTask;

                if (!TryLoadXml(requestBody, out var soapXml, out var xmlErrorTask, req))
                    return await xmlErrorTask;

                var nsmgr = CreateNamespaceManager(soapXml);

                if (!TryGetSubmitIcsResponseNode(soapXml, nsmgr, out var submitIcsResponseNode, out var notFoundTask, req))
                    return await notFoundTask;

                var responseObject = BuildResponseObject(soapXml, nsmgr, submitIcsResponseNode);

                return await WriteJsonResponseAsync(req, responseObject);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in ConvertSoapToJson");
                var errorResp = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                await errorResp.WriteStringAsync("An error occurred while processing the request");
                return errorResp;
            }
        }

        private bool IsValidRequestBody(string requestBody, out Task<HttpResponseData> badRequestTask, HttpRequestData req)
        {
            if (string.IsNullOrWhiteSpace(requestBody))
            {
                badRequestTask = CreateBadRequestAsync(req, "Request body is empty");
                return false;
            }
            badRequestTask = null;
            return true;
        }

        private bool TryLoadXml(string xml, out XmlDocument soapXml, out Task<HttpResponseData> errorTask, HttpRequestData req)
        {
            soapXml = new XmlDocument();
            try
            {
                soapXml.LoadXml(xml);
                errorTask = null;
                return true;
            }
            catch (XmlException ex)
            {
                _logger.LogError(ex, "Invalid XML received");
                errorTask = CreateBadRequestAsync(req, "Invalid XML format");
                return false;
            }
        }

        private bool TryGetSubmitIcsResponseNode(XmlDocument soapXml, XmlNamespaceManager nsmgr, out XmlNode submitIcsResponseNode, out Task<HttpResponseData> notFoundTask, HttpRequestData req)
        {
            submitIcsResponseNode = soapXml.SelectSingleNode("//s:Body/ns0:SubmitICSResponse", nsmgr);
            if (submitIcsResponseNode == null)
            {
                notFoundTask = CreateBadRequestAsync(req, "SOAP Body not found");
                return false;
            }
            notFoundTask = null;
            return true;
        }

        private JObject BuildResponseObject(XmlDocument soapXml, XmlNamespaceManager nsmgr, XmlNode submitIcsResponseNode)
        {
            var innerPayloadNode = submitIcsResponseNode.ChildNodes
                .OfType<XmlNode>()
                .FirstOrDefault(n => n.NodeType == XmlNodeType.Element);

            string responseType = innerPayloadNode?.LocalName ?? submitIcsResponseNode.LocalName;
            var responseObject = InitializeResponseObject(responseType);

            PopulateCreditRequestNumber(soapXml, nsmgr, responseObject);

            XmlNodeList externalEntities = submitIcsResponseNode.SelectNodes(".//ns0:ExternalLegalEntity", nsmgr);
            if (externalEntities != null && externalEntities.Count > 0)
            {
                PopulateExternalEntities(responseObject, externalEntities);
            }

            XmlNode returnStatusNode = submitIcsResponseNode.SelectSingleNode(".//ns0:Results/ns0:ReturnStatus", nsmgr);
            if (returnStatusNode != null)
            {
                responseObject["Input"]["ReturnStatus"] = returnStatusNode.InnerText;
            }

            return responseObject;
        }

        private async Task<HttpResponseData> WriteJsonResponseAsync(HttpRequestData req, JObject responseObject)
        {
            var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");
            await response.WriteStringAsync(responseObject.ToString(Formatting.Indented));
            return response;
        }
        private async Task<string> ReadRequestBodyAsync(HttpRequestData req)
        {
            using var reader = new StreamReader(req.Body, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }

        private XmlNamespaceManager CreateNamespaceManager(XmlDocument doc)
        {
            var nsmgr = new XmlNamespaceManager(doc.NameTable);
            nsmgr.AddNamespace("s", SoapNamespace);
            nsmgr.AddNamespace("ns0", IcsNamespace);
            nsmgr.AddNamespace("nsp2", HeaderNamespace);
            return nsmgr;
        }

        private JObject InitializeResponseObject(string responseType)
        {
            return new JObject(
                    new JProperty("Input", new JObject(
                    new JProperty("ICSSearchDataResponseInput", new JArray()),
                    new JProperty("CreditRequestNumber", ""),
                    new JProperty("ICSResponseType", responseType)
                ))
            );
        }

        private void PopulateCreditRequestNumber(XmlDocument doc, XmlNamespaceManager nsmgr, JObject responseObject)
        {
            XmlNode requestorNode = doc.SelectSingleNode("//s:Header/nsp2:Requestor/nsp2:Reference", nsmgr);
            XmlNode appNumber = doc.SelectSingleNode("//s:Header/nsp2:Requestor/nsp2:ApplicationID", nsmgr);

            if (appNumber != null)
            {
                responseObject["Input"]["CreditRequestNumber"] = appNumber.InnerText;
            }
        }

        private void PopulateExternalEntities(JObject responseObject, XmlNodeList entities)
        {
            var searchDataArray = (JArray)responseObject["Input"]["ICSSearchDataResponseInput"];
            foreach (XmlNode node in entities)
            {
                try
                {
                    var bureauObj = CreateBureauObject(node);
                    searchDataArray.Add(bureauObj);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing ExternalLegalEntity node");
                }
            }
        }

        private JObject CreateBureauObject(XmlNode node)
        {
            XNamespace ns0 = IcsNamespace;
            XElement xElement = XElement.Load(new XmlNodeReader(node));

            return new JObject(
                new JProperty("BureauName", (string)xElement.Element(ns0 + "ExternalIds")?
                                                        .Element(ns0 + "ExternalId")?
                                                        .Element(ns0 + "ExternalIdType") ?? ""),
                new JProperty("BureauCustomerNumber", (string)xElement.Element(ns0 + "ExternalIds")?
                                                              .Element(ns0 + "ExternalId")?
                                                              .Element(ns0 + "ExternalIdValue") ?? ""),
                new JProperty("BureauCustomerName", (string)xElement.Element(ns0 + "Organization")?
                                                              .Element(ns0 + "LegalName") ?? ""),
                new JProperty("DBA", string.Join(", ", xElement.Element(ns0 + "Organization")?
                                                              .Elements(ns0 + "AlternativeNames")?
                                                              .Elements(ns0 + "Name")?
                                                              .Select(n => (string)n) ?? Enumerable.Empty<string>())),
                new JProperty("Address", (string)xElement.Element(ns0 + "Address")?.Element(ns0 + "AddressLine") ?? ""),
                new JProperty("City", (string)xElement.Element(ns0 + "Address")?.Element(ns0 + "City") ?? ""),
                new JProperty("StateProvinceCode", (string)xElement.Element(ns0 + "Address")?.Element(ns0 + "StateProvinceCode") ?? ""),
                new JProperty("PostalCode", (string)xElement.Element(ns0 + "Address")?.Element(ns0 + "PostalCode") ?? ""),
                new JProperty("PhoneNumber", (string)xElement.Element(ns0 + "PhoneRecords")?
                                                         .Element(ns0 + "PhoneRecord")?
                                                         .Element(ns0 + "PhoneNumber") ?? ""),
                new JProperty("ConfidenceIndicator", (decimal?)(int?)xElement.Element(ns0 + "MatchConfidence") ?? 0m),
                new JProperty("IsActive", true)
            );
        }

        private async Task<HttpResponseData> CreateBadRequestAsync(HttpRequestData req, string message)
        {
            var badResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await badResponse.WriteStringAsync(message);
            return badResponse;
        }
    }
}