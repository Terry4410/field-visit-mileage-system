using System.Security.Cryptography;
using System.Text.Json;
using FieldVisit.Infrastructure;

namespace FieldVisit.Infrastructure
{
    public static class UatReleaseIdentity
    {
        public const string SourceSha="1111111111111111111111111111111111111111";
    }
}
internal static class Program
{
    private static readonly byte[] Key=Enumerable.Range(0,32).Select(i=>(byte)(i+1)).ToArray();
    private static readonly DateTimeOffset Now=new(2026,10,24,10,0,0,TimeSpan.Zero);
    private static int tests;
    private static byte[] Payload(V180UatReleaseTrust.Attestation attestation)
        =>JsonSerializer.SerializeToUtf8Bytes(attestation,new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static string Sign(byte[] bytes)=>Convert.ToHexString(HMACSHA256.HashData(Key,bytes)).ToLowerInvariant();
    private static void Accept(string name,V180UatReleaseTrust.Attestation a,V180UatReleaseTrust.Facts f)
    {
        var b=Payload(a);V180UatReleaseTrust.Validate(b,Sign(b),Key,f,Now);
        tests++;Console.WriteLine("UpgradeProfileCase="+name+"; Status=PASS;");
    }
    private static void Reject(string name,V180UatReleaseTrust.Attestation a,V180UatReleaseTrust.Facts f,
        bool invalidateSignature=false,DateTimeOffset? at=null)
    {
        var b=Payload(a);
        try
        {
            V180UatReleaseTrust.Validate(b,invalidateSignature?new string('0',64):Sign(b),Key,f,at??Now);
        }
        catch(InvalidOperationException e) when(e.Message=="OPTIONC_UAT_TRUST_DENIED")
        {
            tests++;Console.WriteLine("UpgradeProfileCase="+name+"; Status=PASS;");return;
        }
        throw new InvalidOperationException("EXPECTED_DENIAL_NOT_OBSERVED");
    }
    private static int Main(string[] args)
    {
        if(args.Length==2 && args[0]=="--sql") return SqlHistoryBridge.Execute(args[1]);
        if(args.Length!=0) return 1;
        try
        {
            var f=new V180UatReleaseTrust.Facts(
                UatReleaseIdentity.SourceSha,new string('a',64),new string('b',64),new string('c',64),
                new string('d',64),"synthetic-principal",V180UatReleaseTrust.Server,
                V180UatReleaseTrust.Database,5,true,true,V180UatReleaseTrust.Features);
            var a=new V180UatReleaseTrust.Attestation(f.CandidateSourceSha,f.ApiSha256,
                f.FrontendSha256,f.SchemaSha256,f.PrincipalSidSha256,f.PrincipalName,
                f.Server,f.Database,f.Features,Now.AddMinutes(-1),Now.AddHours(1));
            Accept("FrozenFreshProfileUnchanged",a,f);
            var upgradeFacts=f with
            {
                SchemaProfile="Existing010Upgrade",
                HistoricalMarkerSha256=new string('e',64),
                PromotionPackageId=new string('1',64),
                LedgerSha256=new string('f',64),
                TableCount=71
            };
            var upgradeAttestation=a with
            {
                SchemaProfile="Existing010Upgrade",
                HistoricalMarkerSha256=upgradeFacts.HistoricalMarkerSha256,
                PromotionPackageId=upgradeFacts.PromotionPackageId,
                LedgerSha256=upgradeFacts.LedgerSha256,
                TableCount=upgradeFacts.TableCount
            };
            Accept("SignedExisting010Upgrade",upgradeAttestation,upgradeFacts);
            Reject("OldFreshSignatureCannotUpgrade",a,upgradeFacts);
            Reject("UpgradeSignatureCannotAuthorizeFresh",upgradeAttestation,f);
            Reject("DifferentHistoricalDigest",upgradeAttestation,upgradeFacts with {HistoricalMarkerSha256=new string('0',64)});
            Reject("DifferentPromotionId",upgradeAttestation,upgradeFacts with {PromotionPackageId=new string('2',64)});
            Reject("DifferentLedgerDigest",upgradeAttestation,upgradeFacts with {LedgerSha256=new string('3',64)});
            Reject("DifferentTableCount",upgradeAttestation,upgradeFacts with {TableCount=72});
            Reject("MissingSignedHistory",upgradeAttestation with {HistoricalMarkerSha256=null},upgradeFacts);
            Reject("MalformedSignedHistory",upgradeAttestation with {HistoricalMarkerSha256="NO"},upgradeFacts);
            Reject("ForbiddenTableCount",upgradeAttestation with {TableCount=69},upgradeFacts with {TableCount=69});
            Reject("TamperedHmac",upgradeAttestation,upgradeFacts,true);
            Reject("ExpiredUpgrade",upgradeAttestation,upgradeFacts,false,Now.AddHours(2));
            Reject("WrongSqlPrincipal",upgradeAttestation,upgradeFacts with {PrincipalName="other"});
            Reject("DdlPrivilegeGranted",upgradeAttestation,upgradeFacts with {NonOwnerNoDdl=false});
            Reject("NonTlsTransport",upgradeAttestation,upgradeFacts with {EncryptedCertificateVerified=false});
            Reject("UnknownUpgradeProfile",upgradeAttestation with {SchemaProfile="Other"},upgradeFacts);

            var pathBFacts=f with {Server=V180UatReleaseTrust.PathBServer,Database=V180UatReleaseTrust.PathBDatabase,SchemaProfile=V180UatReleaseTrust.PathBProfile,TableCount=69,Features=V180UatReleaseTrust.PathBFeatures};
            var pathBAttestation=a with {Server=pathBFacts.Server,Database=pathBFacts.Database,SchemaProfile=V180UatReleaseTrust.PathBProfile,TableCount=69,Features=V180UatReleaseTrust.PathBFeatures};
            Accept("SignedPathBNewEmpty69Table",pathBAttestation,pathBFacts);
            Reject("PathBWrongServer",pathBAttestation,pathBFacts with {Server=V180UatReleaseTrust.Server});
            Reject("PathBLegacyDatabase",pathBAttestation,pathBFacts with {Database=V180UatReleaseTrust.Database});
            Reject("PathBProductionServer",pathBAttestation,pathBFacts with {Server="sql-fieldvisit-jpe-prod.database.windows.net"});
            Reject("PathBProductionDatabase",pathBAttestation,pathBFacts with {Database="db-fieldvisit-prod"});
            Reject("PathBWrongTableCount",pathBAttestation,pathBFacts with {TableCount=68});
            Reject("PathBWrongFeatureFlags",pathBAttestation,pathBFacts with {Features=V180UatReleaseTrust.Features});
            Reject("PathBWrongProfile",pathBAttestation with {SchemaProfile="Existing010Upgrade"},pathBFacts);
            Reject("PathBHistoryMarkerInjection",pathBAttestation with {HistoricalMarkerSha256=new string('e',64)},pathBFacts);
            Reject("PathBTamperedHmac",pathBAttestation,pathBFacts,true);
            Console.WriteLine("PathBReleaseTrust=PASS; Cases=9; RealSQL=NOT_ACCESSED; LiveUAT=HOLD;");
            Console.WriteLine("UpgradeTrustSignedProfile=PASS; Tests="+tests+"; RealSQL=NOT_ACCESSED; LiveUAT=HOLD;");
            return 0;
        }
        catch
        {
            Console.WriteLine("UpgradeTrustSignedProfile=STOP; Details=SUPPRESSED;");
            return 1;
        }
    }
}
