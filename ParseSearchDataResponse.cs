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
        private  ILogger<ParseSearchDataResponse> _logger;

        private const string SoapNamespace = "http://schemas.xmlsoap.org/soap/envelope/";
        private const string IcsNamespace = "http://schemas.delagelanden.com/service/ics/1.0";
        private const string HeaderNamespace = "http://schemas.delagelanden.com/model/soapdllheader/1.0";

        public async Task<HttpResponseData> ProcessLosResponse(string requestBody, HttpRequestData req)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(requestBody))
                    return await CreateBadRequestAsync(req, "Request body is empty");

                var soapXml = new XmlDocument();
                try { soapXml.LoadXml(requestBody); }
                catch (XmlException ex)
                {
                    _logger.LogError(ex, "Invalid XML received");
                    return await CreateBadRequestAsync(req, "Invalid XML format");
                }

                XmlNamespaceManager nsmgr = CreateNamespaceManager(soapXml);

                XmlNode submitIcsResponseNode = soapXml.SelectSingleNode("//s:Body/ns0:SubmitICSResponse", nsmgr);
                if (submitIcsResponseNode == null)
                    return await CreateBadRequestAsync(req, "SOAP Body not found");

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

                var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json");
                await response.WriteStringAsync(responseObject.ToString(Formatting.Indented));
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in ConvertSoapToJson");
                var errorResp = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                await errorResp.WriteStringAsync("An error occurred while processing the request");
                return errorResp;
            }

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
            XmlNode appIdNode = doc.SelectSingleNode("//s:Header/nsp2:Requestor/nsp2:ApplicationID", nsmgr);

            if (requestorNode != null)
            {
                responseObject["Input"]["CreditRequestNumber"] = requestorNode.InnerText;
            }
        }

        private void PopulateExternalEntities(JObject responseObject, XmlNodeList entities)
        {
            var searchDataArray = (JArray)responseObject["Input"]["ICSSearchDataResponseInput"];
            XNamespace ns0 = IcsNamespace;

            foreach (XmlNode node in entities)
            {
                try
                {
                    XElement xElement = XElement.Load(new XmlNodeReader(node));

                    var bureauObj = new JObject(
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

                    searchDataArray.Add(bureauObj);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing ExternalLegalEntity node");
                }
            }
        }

        private async Task<HttpResponseData> CreateBadRequestAsync(HttpRequestData req, string message)
        {
            var badResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await badResponse.WriteStringAsync(message);
            return badResponse;
        }
    }
}
