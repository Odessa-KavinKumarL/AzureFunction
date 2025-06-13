using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using Formatting = Newtonsoft.Json.Formatting;

namespace ConvertSoapToJson
{
    public class ConvertSoapToJson
    {
        private readonly ILogger<ConvertSoapToJson> _logger;

        public ConvertSoapToJson(ILogger<ConvertSoapToJson> logger)
        {
            _logger = logger;
        }
        string ICSResponseType;

        [Function("ConvertSoapToJson")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
        {
            try
            {
                const string soapNamespace = "http://schemas.xmlsoap.org/soap/envelope/";
                const string icsNamespace = "http://schemas.delagelanden.com/service/ics/1.0";

                if (req.Body == null)
                {
                    var badResp = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                    await badResp.WriteStringAsync("Request body is empty");
                    return badResp;
                }

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(requestBody))
                {
                    var badResp = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                    await badResp.WriteStringAsync("Request body is empty");
                    return badResp;
                }

                XmlDocument soapXml = new XmlDocument();
                try
                {
                    soapXml.LoadXml(requestBody);
                }
                catch (XmlException ex)
                {
                    _logger.LogError(ex, "Invalid XML received");
                    var badResp = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                    await badResp.WriteStringAsync("Invalid XML format");
                    return badResp;
                }

                XmlNamespaceManager nsmgr = new XmlNamespaceManager(soapXml.NameTable);
                nsmgr.AddNamespace("s", soapNamespace);
                nsmgr.AddNamespace("ns0", icsNamespace);

                XmlNode bodyNode = soapXml.SelectSingleNode("//s:Body", nsmgr)?.FirstChild;
                if (bodyNode == null)
                {
                    var badResp = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                    await badResp.WriteStringAsync("SOAP Body not found");
                    return badResp;
                }

                ICSResponseType = bodyNode.LocalName;

                XmlNodeList externalLegalEntities = bodyNode.SelectNodes(".//ns0:ExternalLegalEntity", nsmgr);
                if (externalLegalEntities == null || externalLegalEntities.Count == 0)
                {
                    var notFoundResp = req.CreateResponse(System.Net.HttpStatusCode.OK);
                    await notFoundResp.WriteStringAsync("[]");
                    return notFoundResp;
                }

                JArray bureauList = new JArray();
                XNamespace ns0 = icsNamespace;

                foreach (XmlNode entityNode in externalLegalEntities)
                {
                    try
                    {
                        var xElement = XElement.Load(new XmlNodeReader(entityNode));
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
                            new JProperty("Address", (string)xElement.Element(ns0 + "Address")?
                                                                    .Element(ns0 + "AddressLine") ?? ""),
                            new JProperty("City", (string)xElement.Element(ns0 + "Address")?
                                                                 .Element(ns0 + "City") ?? ""),
                            new JProperty("StateProvinceCode", (string)xElement.Element(ns0 + "Address")?
                                                                              .Element(ns0 + "StateProvinceCode") ?? ""),
                            new JProperty("PostalCode", (string)xElement.Element(ns0 + "Address")?
                                                                       .Element(ns0 + "PostalCode") ?? ""),
                            new JProperty("PhoneNumber", (string)xElement.Element(ns0 + "PhoneRecords")?
                                                                       .Element(ns0 + "PhoneRecord")?
                                                                       .Element(ns0 + "PhoneNumber") ?? ""),
                            new JProperty("ConfidenceIndicator", (int?)xElement.Element(ns0 + "MatchConfidence") ?? 0),
                            new JProperty("CreatedById", 1),
                            new JProperty("CreatedTime", DateTime.Now),
                            new JProperty("IsActive", 1),
                            new JProperty("State", (string)xElement.Element(ns0 + "Address")?
                                                                              .Element(ns0 + "StateProvinceCode") ?? ""),
                            new JProperty("CreditRequestStatus", ICSResponseType));

                        bureauList.Add(bureauObj);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing ExternalLegalEntity node");
                    }
                }

                var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json");
                await response.WriteStringAsync(bureauList.ToString(Formatting.Indented));
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
    }
}