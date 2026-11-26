using System.Text.Json;

namespace FieldVisit.Infrastructure;

// Filtering is applied only AFTER the existing live actor/organization/own-request predicate.
public sealed record V180OwnLocationRequestFilter(DateOnly? Start, DateOnly? End, string? Q, string? Kind, string? Status)
{
    public void Validate()
    {
        if (Start.HasValue && End.HasValue && Start.Value > End.Value ||
            Start.HasValue && Start.Value.Year < 1900 || End.HasValue && (End.Value.Year < 1900 || End.Value.Year >= 9999) ||
            Q?.Length > 200 || Kind is not (null or "" or "all" or "create" or "tax" or "change" or "other") ||
            Status is not (null or "" or "all" or "pending" or "completed" or "rejected"))
            throw new InvalidOperationException("請確認申請日期、搜尋文字及查詢條件。");
    }
    public string? StatusCode => Status switch { "pending" => "Pending", "completed" => "Applied", "rejected" => "Rejected", _ => null };
    public static readonly string[] Fields = ["LocationName","City","District","Address","PlusCode","TaxId","MasterNote"];
    public static string Label(string key) => key switch { "LocationName"=>"名稱","City"=>"縣市","District"=>"鄉鎮區","Address"=>"地址","PlusCode"=>"Plus Code","TaxId"=>"統一編號","MasterNote"=>"地點備註",_=>key };
    public static Dictionary<string,string> Read(string? json)
    {
        var result = Fields.ToDictionary(k=>k,_=>string.Empty,StringComparer.Ordinal);
        if (string.IsNullOrEmpty(json) || json.Length > 10000) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if(doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach(var key in Fields)
                if(doc.RootElement.TryGetProperty(key,out var value) && value.ValueKind == JsonValueKind.String)
                    result[key] = value.GetString() ?? "";
        }
        catch(JsonException) { }
        return result;
    }
    public static string Category(V180B3RequestView r)
    {
        if(r.OperationCode == "CreateLocation") return "create";
        if(r.OperationCode != "UpdatePublishedLocation") return "other";
        var before=Read(r.BeforeJson); var after=Read(r.ProposedJson);
        var changed=Fields.Where(k=>before[k]!=after[k]).ToArray();
        return changed.Length==1 && changed[0]=="TaxId" ? "tax" : "change";
    }
    public static string TypeLabel(V180B3RequestView r) => Category(r) switch { "create"=>"新增地點","tax"=>"統編補登／異動","change"=>"地點異動",_=>"其他申請" };
    public static string Name(V180B3RequestView r)
    {
        var a=Read(r.ProposedJson)["LocationName"]; var b=Read(r.BeforeJson)["LocationName"];
        return a.Length>0?a:b.Length>0?b:"地點資料";
    }
    public bool Matches(V180B3RequestView r)
    {
        if(!string.IsNullOrEmpty(Kind)&&Kind!="all"&&Kind!=Category(r)) return false;
        var before=Read(r.BeforeJson);var after=Read(r.ProposedJson);
        var hay=string.Join(" ",Name(r),before["Address"],after["Address"],before["TaxId"],after["TaxId"]);
        return hay.Contains(Q?.Trim()??"",StringComparison.OrdinalIgnoreCase);
    }
}
