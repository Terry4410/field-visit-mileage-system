namespace FieldVisit.Application;

/// <summary>Only address/PlusCode require geocoding. Name and TaxId
/// additionally affect duplicate matching. Notes do neither.</summary>
public static class V180LocationMaterialChangeRules
{
    private static string Normalize(string? text)=>text?.Trim()??"";
    private static bool Changed(string? before,string? after)=>
        !string.Equals(Normalize(before),Normalize(after),StringComparison.Ordinal);
    public static bool RequiresGeocoding(string? oldAddress,string? oldPlus,
        string? newAddress,string? newPlus)=>
        Changed(oldAddress,newAddress)||Changed(oldPlus,newPlus);
    public static bool RequiresDuplicateRecheck(string? oldName,string? oldAddress,
        string? oldPlus,string? oldTax,string? newName,string? newAddress,
        string? newPlus,string? newTax)=>
        Changed(oldName,newName)||RequiresGeocoding(oldAddress,oldPlus,newAddress,newPlus)
        ||Changed(oldTax,newTax);
}
