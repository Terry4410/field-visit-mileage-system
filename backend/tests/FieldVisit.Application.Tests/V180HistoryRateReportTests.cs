using FieldVisit.Application;
using FieldVisit.Infrastructure;
using Microsoft.Extensions.Configuration;
using NPOI.XSSF.UserModel;
using NPOI.SS.UserModel;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180HistoryRateReportTests
{
    [Fact]
    public void Rate_query_uses_overlap_vehicle_status_and_keeps_open_ended_versions()
    {
        MileageRateDto[] rates = [
            new(1,1,"Test old","Motorcycle",2.5m,new(2026,1,1),new(2026,12,31),true),
            new(2,1,"Test future","Motorcycle",3m,new(2027,1,1),null,true),
            new(3,1,"Test disabled","Car",4m,new(2026,1,1),null,false),
            new(4,null,"Shared","Car",5m,new(2025,1,1),null,true)];
        var filtered=V180MileageRateQueryRules.Filter(rates,new(Keyword:"test",Status:"Active",VehicleType:"Motorcycle",StartDate:new(2026,12,31),EndDate:new(2027,1,1)));
        Assert.Equal(new[]{2,1},filtered.Select(x=>x.MileageRateRuleId));
        Assert.Equal(2,Assert.Single(V180MileageRateQueryRules.Filter(rates,new(VehicleType:"Motorcycle",StartDate:new(2027,2,1)))).MileageRateRuleId);
        Assert.Equal(3,Assert.Single(V180MileageRateQueryRules.Filter(rates,new(Status:"Inactive"))).MileageRateRuleId);
        Assert.Equal(4,V180MileageRateQueryRules.Filter(rates,new()).Count);
        Assert.Throws<InvalidOperationException>(()=>V180MileageRateQueryRules.Filter(rates,new(Status:"wrong")));
        Assert.Throws<InvalidOperationException>(()=>V180MileageRateQueryRules.Filter(rates,new(VehicleType:"wrong")));
        Assert.Throws<InvalidOperationException>(()=>V180MileageRateQueryRules.Filter(rates,new(StartDate:new(2027,2,1),EndDate:new(2027,1,1))));
    }

    [Theory]
    [InlineData("更正歷史")]
    [InlineData("補助費率")]
    public async Task Both_formats_keep_all_rows_Chinese_and_safe_cells(string title)
    {
        var service=new ReportDocumentService(new ConfigurationBuilder().Build());
        var actor=new CurrentUserDto(1,"00123","測試管理者",null,1,1,"測試小組",["admin"]);
        bool isRate=title=="補助費率";
        string[] headers=isRate?["交通工具","生效日期","失效日期","每公里補助","規則名稱／備註","狀態","範圍"]:
            ["申請日","行程編號","外訪員","更正原因","狀態","原核准版本","結果版本"];
        float[] widths=isRate?[90f,105f,105f,105f,230f,75f,75.89f]:[86f,120f,100f,249f,95f,70f,65.89f];
        var textIndex=isRate?4:3;
        var rows=Enumerable.Range(1,125).Select(i=>{
            var reason=i==1?"=1+1":i==125?"最後一筆確認":new string('更',100);
            return isRate?new object?[]{"機車","2026-11-26","無期限",2.5m,reason,"啟用","組織費率"}:
                new object?[]{"2026-11-26",$"T{i:000}","測試外訪員",reason,"已結案","V1","V2"};
        }).ToArray();
        var excel=await service.CreateTabularAsync("xlsx",title,headers,widths,rows,"全部狀態",actor,default);
        using(var book=new XSSFWorkbook(new MemoryStream(excel.Content))){
            var sheet=book.GetSheet("查詢結果");
            Assert.Equal(125,sheet.LastRowNum);
            Assert.Equal("最後一筆確認",sheet.GetRow(125).GetCell(textIndex).StringCellValue);
            Assert.Equal(CellType.String,sheet.GetRow(1).GetCell(textIndex).CellType);
            Assert.Equal("=1+1",sheet.GetRow(1).GetCell(textIndex).StringCellValue);
            if(isRate)Assert.Equal(2.5,sheet.GetRow(1).GetCell(3).NumericCellValue);
        }
        var pdf=await service.CreateTabularAsync("pdf",title,headers,widths,rows,"全部狀態",actor,default);
        Assert.StartsWith("%PDF-",System.Text.Encoding.ASCII.GetString(pdf.Content.AsSpan(0,5)));
        Assert.True(pdf.Content.Length>10000);
        var dir=Environment.GetEnvironmentVariable("PATHB_PDF_VERIFY_DIR");
        if(!string.IsNullOrWhiteSpace(dir)){Directory.CreateDirectory(dir);File.WriteAllBytes(Path.Combine(dir,title+".pdf"),pdf.Content);}
    }
}
