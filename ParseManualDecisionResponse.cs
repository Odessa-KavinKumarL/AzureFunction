using Microsoft.Azure.Functions.Worker.Http;
using System.Text.Json;
using System.Xml.Linq;

public class ParseManualDecisionResponse
{
    public async Task<HttpResponseData> ProcessManualDecisionResponse(string soapXmlString, HttpRequestData req)
    {
        var xdoc = XDocument.Parse(soapXmlString);

        var ns = "http://schemas.delagelanden.com/service/ics/1.0";

        var manualDecisionResult = xdoc.Descendants("ICSManualDecisionResult").FirstOrDefault();
        var response = manualDecisionResult?.Element("Response");

        var obtainManualDecisionResponse = response?.Element(XName.Get("ObtainManualDecisionResponse", ns));
        var application = obtainManualDecisionResponse?.Element(XName.Get("Application", ns));

        var link = application?.Element(XName.Get("Links", ns))?.Element(XName.Get("Link", ns));
        var legalEntity = application?.Element(XName.Get("LegalEntity", ns));

        var creditRequestNumber = application?.Element(XName.Get("ApplicationId", ns))?.Value;
        var returnStatus = response?.Element(XName.Get("Results", ns))?.Element(XName.Get("ReturnStatus", ns))?.Value;

        var input = new SubmitICSManualDecisionResponseOutput
        {
            Input = new SubmitICSManualDecisionResponseInput
            {
                ICSManualDecisionResponseInput = new ICSManualDecisionResponseInput
                {
                    LinkType = link?.Element(XName.Get("LinkType", ns))?.Value,
                    LinkValue = link?.Element(XName.Get("LinkValue", ns))?.Value,
                    RiskGradeRating = manualDecisionResult?.Element("RiskGradeRating")?.Value,
                    TransactionId = manualDecisionResult?.Element("TransactionId")?.Value
                },
                CreditRequestNumber = creditRequestNumber,
                ICSResponseType = "ICSManualDecisionResponse",
                ReturnStatus = returnStatus
            }
        };

        var responseData = req.CreateResponse();
        responseData.Headers.Add("Content-Type", "application/json");
        await responseData.WriteStringAsync(JsonSerializer.Serialize(input));
        return responseData;
    }

    public class SubmitICSManualDecisionResponseOutput
    {
        public SubmitICSManualDecisionResponseInput Input { get; set; }
    }

    public class SubmitICSManualDecisionResponseInput
    {
        public ICSManualDecisionResponseInput ICSManualDecisionResponseInput { get; set; }
        public string CreditRequestNumber { get; set; }
        public string ICSResponseType { get; set; }
        public string ReturnStatus { get; set; }
    }

    public class ICSManualDecisionResponseInput
    {
        public string LinkType { get; set; }
        public string LinkValue { get; set; }
        public string RiskGradeRating { get; set; }
        public string TransactionId { get; set; }
    }
}
