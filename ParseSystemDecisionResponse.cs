using Microsoft.Azure.Functions.Worker.Http;
using System.Text.Json;
using System.Xml.Linq;

public class ParseSystemDecisionResponse
{
    public async Task<HttpResponseData> ProcessObtainSystemResponse(string soapXmlString, HttpRequestData req)
    {
        var xdoc = XDocument.Parse(soapXmlString);
        XNamespace q1 = "http://schemas.delagelanden.com/service/ics/1.0";
        XNamespace common = "http://schemas.delagelanden.com/common";

        var submitICSResponse = xdoc.Descendants(q1 + "SubmitICSResponse").FirstOrDefault();
        var obtainDecisionResponse = submitICSResponse?.Element(q1 + "ObtainDecisionResponse");
        var application = obtainDecisionResponse?.Element(q1 + "Application");
        var systemDecision = application?.Element(q1 + "SystemDecision");
        var approvalCondition = systemDecision?.Element(q1 + "ApprovalConditions")?.Element(q1 + "ApprovalCondition");
        var link = application?.Element(q1 + "Links")?.Element(q1 + "Link");

        var internalLegalEntity = obtainDecisionResponse?.Element(q1 + "Customer")?.Element(q1 + "InternalLegalEntity");
        var externalLegalEntity = obtainDecisionResponse?.Element(q1 + "Customer")?.Element(q1 + "ExternalLegalEntities")?.Element(q1 + "ExternalLegalEntity");

        var applicableRating = internalLegalEntity?.Element(q1 + "ApplicableRating");
        var defaultIndicator = internalLegalEntity?.Element(q1 + "Default");

        var externalId = externalLegalEntity?.Element(q1 + "ExternalIds")?.Element(q1 + "ExternalId");

        var scoreCards = application?.Element(q1 + "ScoreResult")?.Element(q1 + "ScorecardResults")?.Elements(q1 + "ScorecardResult")
                        .Select(sc => new ScoreCards
                        {
                            ScoreCardName = sc.Element(q1 + "ScorecardName")?.Value,
                            ScoreCardScore = ParseInt(sc.Element(q1 + "ScorecardScore")?.Value)
                        }).ToList();

        var reasonCodes = systemDecision?.Element(q1 + "DecisionReasons")?.Elements(q1 + "DecisionReason")
            .Select(rc => new ReasonCodes
            {
                ReasonCode = rc.Element(q1 + "ReasonCode")?.Value,
                ReasonCodeDescription = rc.Element(q1 + "ReasonDesc")?.Value
            })
            .ToList();


        var input = new SubmitICSResponseOutput
        {
            Input = new SubmitICSResponseInput
            {
                ICSObtainSystemDecisionResponseInput = new ICSObtainSystemDecisionResponseInput
                {
                    SystemApprovalLimit = ParseDecimal(systemDecision?.Element(q1 + "SystemApprovalLimit")?.Value),
                    SystemDecision = systemDecision?.Element(q1 + "SystemDecision")?.Value,
                    ScoreDecision = systemDecision?.Element(q1 + "ScoreDecision")?.Value,
                    SystemDecisionDate = ParseDateTime(systemDecision?.Element(q1 + "SystemDecisionDate")?.Value),
                    ApprovalConditionCode = approvalCondition?.Element(q1 + "ApprovalConditionCode")?.Value,
                    ApprovalConditionDescription = approvalCondition?.Element(q1 + "ApprovalConditionDesc")?.Value,
                    LinkType = link?.Element(q1 + "LinkType")?.Value,
                    LinkValue = link?.Element(q1 + "LinkValue")?.Value,
                    CREStatus = internalLegalEntity?.Element(q1 + "CREStatus")?.Value,
                    LegalName = internalLegalEntity?.Element(q1 + "LegalName")?.Value ??
                                externalLegalEntity?.Element(q1 + "Organization")?.Element(q1 + "LegalName")?.Value,
                    PDRequiredTreatment = internalLegalEntity?.Element(q1 + "PDRequiredTreatment")?.Value,
                    PDTreatmentExposureAmountInEuro = ParseDecimal(internalLegalEntity?.Element(q1 + "PDTreatmentExposureAmountInEuro")?.Value),
                    RiskGradeRating = internalLegalEntity?.Element(q1 + "RiskGradeRating")?.Value,
                    CurrentlyInDefaultIndicator = ParseBool(defaultIndicator?.Element(q1 + "CurrentlyInDefaultIndicator")?.Value),
                    DefaultDate = ParseDateTime(defaultIndicator?.Element(q1 + "DefaultDate")?.Value),
                    InitialDefaultReason = defaultIndicator?.Element(q1 + "InitialDefaultReason")?.Value,
                    MasterscaleRating = defaultIndicator?.Element(q1 + "MasterscaleRating")?.Value,
                    Severity = defaultIndicator?.Element(q1 + "Severity")?.Value,
                    PDLookupType = applicableRating?.Element(q1 + "PDLookupType")?.Value,
                    PDPercentage = ParseDecimal(applicableRating?.Element(q1 + "PDPercentage")?.Value),
                    PDRatingDate = ParseDateTime(applicableRating?.Element(q1 + "PDRatingDate")?.Value),
                    PDRatingModel = applicableRating?.Element(q1 + "PDRatingModel")?.Value,
                    PDRatingScore = ParseInt(applicableRating?.Element(q1 + "PDRatingScore")?.Value),
                    RabobankMasterscaleRating = applicableRating?.Element(q1 + "RabobankMasterscaleRating")?.Value,
                    ExternalIdType = externalId?.Element(q1 + "ExternalIdType")?.Value,
                    ExternalIdValue = ParseLong(externalId?.Element(q1 + "ExternalIdValue")?.Value),
                    TransactionId = null,
                    ScoreCards = scoreCards ?? new List<ScoreCards>(),
                    ReasonCodes = reasonCodes ?? new List<ReasonCodes>()
                },
                CreditRequestNumber = application?.Element(q1 + "ApplicationId")?.Value,
                ICSResponseType = "ObtainDecisionResponse",
                ReturnStatus = submitICSResponse?.Element(q1 + "Results")?.Element(q1 + "ReturnStatus")?.Value
            }
        };

        var response = req.CreateResponse();
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(JsonSerializer.Serialize(input));
        return response;
    }

    private decimal? ParseDecimal(string value) =>
        decimal.TryParse(value, out var result) ? result : null;

    private int? ParseInt(string value) =>
        int.TryParse(value, out var result) ? result : null;

    private long? ParseLong(string value) =>
        long.TryParse(value, out var result) ? result : null;

    private DateTime? ParseDateTime(string value) =>
        DateTime.TryParse(value, out var result) ? result : null;

    private bool? ParseBool(string value) =>
        bool.TryParse(value, out var result) ? result : null;

    public class SubmitICSResponseOutput
    {
        public string Name { get; set; } = "SubmitICSResponse";
        public SubmitICSResponseInput Input { get; set; }
    }

    public class SubmitICSResponseInput
    {
        public ICSObtainSystemDecisionResponseInput ICSObtainSystemDecisionResponseInput { get; set; }
        public string CreditRequestNumber { get; set; }
        public string ICSResponseType { get; set; }
        public string ReturnStatus { get; set; }
    }

    public class ICSObtainSystemDecisionResponseInput
    {
        public decimal? SystemApprovalLimit { get; set; }
        public string SystemDecision { get; set; }
        public string ScoreDecision { get; set; }
        public DateTime? SystemDecisionDate { get; set; }
        public string ApprovalConditionCode { get; set; }
        public string ApprovalConditionDescription { get; set; }
        public string LinkType { get; set; }
        public string LinkValue { get; set; }
        public string CREStatus { get; set; }
        public string LegalName { get; set; }
        public string PDRequiredTreatment { get; set; }
        public decimal? PDTreatmentExposureAmountInEuro { get; set; }
        public string RiskGradeRating { get; set; }
        public bool? CurrentlyInDefaultIndicator { get; set; }
        public DateTime? DefaultDate { get; set; }
        public string InitialDefaultReason { get; set; }
        public string MasterscaleRating { get; set; }
        public string Severity { get; set; }
        public string PDLookupType { get; set; }
        public decimal? PDPercentage { get; set; }
        public DateTime? PDRatingDate { get; set; }
        public string PDRatingModel { get; set; }
        public int? PDRatingScore { get; set; }
        public string RabobankMasterscaleRating { get; set; }
        public string ExternalIdType { get; set; }
        public long? ExternalIdValue { get; set; }
        public int? TransactionId { get; set; }
        public List<ScoreCards> ScoreCards { get; set; }
        public List<ReasonCodes> ReasonCodes { get; set; }
    }
    public class ScoreCards
    {
        public string ScoreCardName { get; set; }
        public int? ScoreCardScore { get; set; }
    }

    public class ReasonCodes
    {
        public string ReasonCode { get; set; }
        public string ReasonCodeDescription { get; set; }
    }
}
