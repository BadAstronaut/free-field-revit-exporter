using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using FreeField.Exporter.Core.Model;

namespace FreeField.Exporter.Core.Tests;

public class JsonTests
{
    private static readonly BuildOptions Build = new();
    private static readonly JsonSchema Main = JsonSchema.FromText(File.ReadAllText(TestUtil.RepoFile("schema", "ffbundle.schema.json")), Build);
    private const string SchemaUri = "https://schemas.freefield.app/ffbundle/1/ffbundle.schema.json";
    private static readonly EvaluationOptions EvalOpts = new() { OutputFormat = OutputFormat.List };

    private static EvaluationResults Eval(JsonSchema s, JsonNode? n) => s.Evaluate(JsonSerializer.SerializeToElement(n), EvalOpts);

    private static EvaluationResults Validate(string def, string json)
    {
        var wrapper = JsonSchema.FromText($"{{\"$ref\":\"{SchemaUri}#/$defs/{def}\"}}", Build);
        return Eval(wrapper, JsonNode.Parse(json));
    }

    private static string Errors(EvaluationResults r) =>
        string.Join("; ", (r.Details ?? new List<EvaluationResults>()).Where(d => d.Errors is not null).SelectMany(d => d.Errors!.Select(e => d.InstanceLocation + " " + e.Value)));

    private static string KeysOf(JsonNode n) => string.Join(",", ((JsonObject)n).Select(kv => kv.Key));

    [Fact]
    public void Manifest_PropertyNames_MatchSpec()
    {
        var m = TestUtil.SampleManifest(out _, out _);
        var o = JsonNode.Parse(BundleJson.Serialize(m))!;
        Assert.Equal("schema,generator,exportedAt,project,units,coordinates,models,sheets,schedules,elementsFile,stats", KeysOf(o));
        Assert.Equal("name,version,host,hostVersion", KeysOf(o["generator"]!));
        Assert.Equal("name,number,sourceFile", KeysOf(o["project"]!));
        Assert.Equal("length,pdf", KeysOf(o["units"]!));
        Assert.Equal("modelSpace,ifcPlacement,trueNorthRad", KeysOf(o["coordinates"]!));
        Assert.Equal("id,file,name,discipline,storeys", KeysOf(o["models"]![0]!));
        Assert.Equal("name,elevation,levelUniqueId,ifcGlobalId", KeysOf(o["models"]![0]!["storeys"]![0]!));
        Assert.Equal("id,number,name,file,data,page,viewports", KeysOf(o["sheets"]![0]!));
        Assert.Equal("widthPt,heightPt", KeysOf(o["sheets"]![0]!["page"]!));
        var vp = o["sheets"]![0]!["viewports"]![0]!;
        Assert.Equal("id,viewId,viewName,viewType,scale,modelId,storeyName,primary,pdfRegion,pdfToModel,controlPoints", KeysOf(vp));
        Assert.Equal("pdfX,pdfY,modelX,modelY", KeysOf(vp["controlPoints"]![0]!));
        Assert.Equal("elements,sheets,viewports,links", KeysOf(o["stats"]!));
    }

    [Fact]
    public void NonPlanViewport_SerializesNullPdfToModel_AndOmitsStorey()
    {
        var m = TestUtil.SampleManifest(out _, out _);
        var vp = JsonNode.Parse(BundleJson.Serialize(m))!["sheets"]![0]!["viewports"]![1]!;
        Assert.True(((JsonObject)vp).ContainsKey("pdfToModel"));
        Assert.Null(vp["pdfToModel"]);
        Assert.False(((JsonObject)vp).ContainsKey("storeyName"));
        Assert.False(((JsonObject)vp).ContainsKey("controlPoints"));
    }

    [Fact]
    public void ElementRecord_And_ParamValue_ShortKeys()
    {
        TestUtil.SampleManifest(out var els, out _);
        var o = JsonNode.Parse(BundleJson.Serialize(els))!;
        Assert.Equal("gid1,gid2", KeysOf(o));
        Assert.Equal("uid,eid,gid,category,bic,family,type,level,modelId,params", KeysOf(o["gid1"]!));
        Assert.Equal("v,d,u,t,k", KeysOf(o["gid1"]!["params"]!["Width"]!));
        Assert.Equal("v,d,t,k", KeysOf(o["gid1"]!["params"]!["Mark"]!)); // null u omitted
    }

    [Fact]
    public void ParamValue_ValueTypes_RoundTrip()
    {
        TestUtil.SampleManifest(out var els, out _);
        var back = BundleJson.Deserialize<Dictionary<string, ElementRecord>>(BundleJson.Serialize(els));
        var p = back["gid1"].Params;
        Assert.Equal("D1", Assert.IsType<string>(p["Mark"].V));
        Assert.Equal(0.9, Assert.IsType<double>(p["Width"].V));
        Assert.Equal(3L, Assert.IsType<long>(p["Count"].V));
        Assert.True(Assert.IsType<bool>(p["Fire"].V));
        Assert.True(p["Width"].T);
    }

    [Fact]
    public void Manifest_ValidatesAgainstSchema()
    {
        var m = TestUtil.SampleManifest(out var els, out var idx);
        // fill what BundleWriter would fill
        m.Models[0].File = "models/arch.ifc";
        m.Sheets[0].File = "sheets/A_103_x.pdf"; m.Sheets[0].Data = "data/sheets/A_103_x.json";
        m.Stats = new BundleStats { Elements = 2, Sheets = 1, Viewports = 2, Links = 1 };
        var main = Eval(Main, JsonNode.Parse(BundleJson.Serialize(m)));
        Assert.True(main.IsValid, Errors(main));
    }

    [Fact]
    public void Manifest_Invalid_IsRejected()
    {
        var m = TestUtil.SampleManifest(out _, out _);
        m.Models[0].File = "models/arch.ifc";
        m.Sheets[0].File = "sheets/a.pdf"; m.Sheets[0].Data = "data/sheets/a.json";
        var node = JsonNode.Parse(BundleJson.Serialize(m))!;
        node["sheets"]![0]!["viewports"]![0]!["pdfToModel"] = new JsonArray(1, 2, 3);
        var r = Eval(Main, node);
        Assert.False(r.IsValid);

        var node2 = JsonNode.Parse(BundleJson.Serialize(m))!;
        node2["sheets"]![0]!["viewports"]![0]!.AsObject().Remove("storeyName");
        Assert.False(Eval(Main, node2).IsValid);
    }

    [Fact]
    public void DataFiles_ValidateAgainstSchemaDefs()
    {
        TestUtil.SampleManifest(out var els, out var idx);
        var r1 = Validate("ElementsFile", BundleJson.Serialize(els));
        Assert.True(r1.IsValid, Errors(r1));
        var r2 = Validate("SheetElementIndex", BundleJson.Serialize(idx[0]));
        Assert.True(r2.IsValid, Errors(r2));
        var table = new DataTable
        {
            Name = "Door Schedule", Category = "Doors",
            Columns = { new DataColumn { Heading = "Mark", ParamKey = "ALL_MODEL_MARK", FieldType = "instance" } },
            Rows = { new DataRow { Gid = "gid1", Uid = "uid1", Cells = { "D1" } } },
        };
        var r3 = Validate("DataTable", BundleJson.Serialize(table));
        Assert.True(r3.IsValid, Errors(r3));
        Assert.False(Validate("DataTable", "{\"name\":\"x\",\"columns\":[{\"heading\":\"a\",\"paramKey\":\"b\",\"fieldType\":\"bad\"}],\"rows\":[]}").IsValid);
    }

    [Fact]
    public void Schema_And_Types_HaveNoVendorNames()
    {
        foreach (var f in new[] { "ffbundle.schema.json", "ffbundle.d.ts" })
        {
            var t = File.ReadAllText(TestUtil.RepoFile("schema", f));
            foreach (var w in new[] { "speckle", "dalux", "revit", "autodesk" })
                Assert.DoesNotContain(w, t, StringComparison.OrdinalIgnoreCase);
        }
    }
}
