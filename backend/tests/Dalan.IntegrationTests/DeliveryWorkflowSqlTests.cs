using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dalan.Contracts.Dispatch;
using Dalan.Domain.Drivers;
using Dalan.Domain.Vehicles;
using Dalan.Infrastructure.Persistence;
using Dalan.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dalan.IntegrationTests;

[Collection("SQL Server")]
public sealed class DeliveryWorkflowSqlTests(SqlServerFixture fixture)
{
    WebApplicationFactory<WebAssemblyMarker> Factory()=>new WebApplicationFactory<WebAssemblyMarker>().WithWebHostBuilder(b=>b.UseEnvironment("Testing").UseSetting("ConnectionStrings:Dalan",fixture.ConnectionString));
    static HttpClient Client(WebApplicationFactory<WebAssemblyMarker> factory)=>factory.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});
    static async Task<(string Email,string Password)> Staff(WebApplicationFactory<WebAssemblyMarker> factory,string role="Dispatcher")
    {
        using var scope=factory.Services.CreateScope();
        var users=scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>(); var roles=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if(!await roles.RoleExistsAsync(role)) Assert.True((await roles.CreateAsync(new(role))).Succeeded);
        var email=Guid.NewGuid().ToString("N")+"@dalan.test"; var password="Fixture!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(16))+"a7";
        var user=new IdentityUser{UserName=email,Email=email,EmailConfirmed=true}; Assert.True((await users.CreateAsync(user,password)).Succeeded); Assert.True((await users.AddToRoleAsync(user,role)).Succeeded);
        return(email,password);
    }
    static string Csrf(string html)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    static async Task Login(HttpClient client,(string Email,string Password) staff)
    {
        var html=await client.GetStringAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"Email",staff.Email},{"Password",staff.Password},{"__RequestVerificationToken",Csrf(html)}}))).StatusCode);
    }
    static async Task<HttpResponseMessage> SaveWarehouse(HttpClient client,string name)
    {
        var html=await client.GetStringAsync("/Warehouse");
        return await client.PostAsync("/Warehouse",new FormUrlEncodedContent(new Dictionary<string,string>{{"Name",name},{"Latitude","14.6"},{"Longitude","121.03"},{"__RequestVerificationToken",Csrf(html)}}));
    }
    [Fact]
    public async Task Warehouse_is_persistent_per_dispatcher_and_requires_authorization_and_csrf()
    {
        using var factory=Factory(); var staff=await Staff(factory); using var first=Client(factory); await Login(first,staff);
        Assert.Equal(HttpStatusCode.BadRequest,(await first.PostAsync("/Warehouse",new FormUrlEncodedContent(new Dictionary<string,string>{{"Name","Bad"},{"Latitude","14.6"},{"Longitude","121.03"}}))).StatusCode);
        var workspaceWarehouse = await first.GetStringAsync("/Warehouse?workspace=true");
        Assert.Contains("Back to dispatch", workspaceWarehouse);
        var savedFromWorkspace = await first.PostAsync("/Warehouse", new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["Name"]="Personal depot", ["Latitude"]="14.6", ["Longitude"]="121.03", ["ReturnToDispatch"]="true",
            ["__RequestVerificationToken"]=Csrf(workspaceWarehouse)
        }));
        Assert.Equal(HttpStatusCode.Redirect, savedFromWorkspace.StatusCode);
        Assert.Equal("/DispatchBoard", savedFromWorkspace.Headers.Location?.ToString());
        using var secondSession=Client(factory); await Login(secondSession,staff);
        Assert.Equal("Personal depot",(await secondSession.GetFromJsonAsync<WarehousePreference>("/Warehouse/Current"))!.Name);
        using var other=Client(factory); await Login(other,await Staff(factory)); Assert.Equal("null",await other.GetStringAsync("/Warehouse/Current"));
        using var admin=Client(factory); await Login(admin,await Staff(factory,"Admin"));
        Assert.Contains("Account/Denied",(await admin.GetAsync("/Warehouse")).Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Redirect,(await SaveWarehouse(secondSession,"Changed depot")).StatusCode);
        Assert.Equal("Changed depot",(await first.GetFromJsonAsync<WarehousePreference>("/Warehouse/Current"))!.Name);
    }
    [Fact]
    public async Task Batch_uses_saved_pickup_and_invalid_rows_create_nothing()
    {
        using var factory=Factory(); using var client=Client(factory); await Login(client,await Staff(factory)); await SaveWarehouse(client,"Batch depot");
        var suffix=Guid.NewGuid().ToString("N"); var html=await client.GetStringAsync("/DeliveryBatch");
        Dictionary<string,string> Fields(bool invalid)=>new() { ["__RequestVerificationToken"]=Csrf(html),["Warehouse.Name"]="Forged pickup",["Warehouse.Latitude"]="0",
            ["Rows[0].RecipientName"]="Batch One "+suffix,["Rows[0].DropoffAddress"]="Address One",["Rows[0].DropoffLatitude"]="14.61",["Rows[0].DropoffLongitude"]="121.04",
            ["Rows[1].RecipientName"]="Batch Two "+suffix,["Rows[1].DropoffAddress"]="Address Two",["Rows[1].DropoffLatitude"]=invalid?"999":"14.62",["Rows[1].DropoffLongitude"]="121.05" };
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsync("/DeliveryBatch",new FormUrlEncodedContent(Fields(true)))).StatusCode);
        await using(var db=fixture.CreateContext()) Assert.Empty(await db.Deliveries.Where(x=>x.RecipientName.EndsWith(suffix)).ToListAsync());
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/DeliveryBatch",new FormUrlEncodedContent(Fields(false)))).StatusCode);
        await using(var db=fixture.CreateContext()) { var created=await db.Deliveries.Where(x=>x.RecipientName.EndsWith(suffix)).ToListAsync(); Assert.Equal(2,created.Count); Assert.All(created,x=>{Assert.Equal("Batch depot",x.PickupAddress);Assert.Equal(14.6,x.PickupLatitude);}); }
        await SaveWarehouse(client,"New depot");
        await using(var db=fixture.CreateContext()) Assert.All(await db.Deliveries.Where(x=>x.RecipientName.EndsWith(suffix)).ToListAsync(),x=>Assert.Equal("Batch depot",x.PickupAddress));
    }
    [Fact]
    public async Task Batch_without_saved_warehouse_cannot_create_work()
    {
        using var factory=Factory(); using var client=Client(factory); await Login(client,await Staff(factory));
        var html=await client.GetStringAsync("/DeliveryBatch"); Assert.Contains("Set your warehouse first",html);
        var response=await client.PostAsync("/DeliveryBatch",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",Csrf(await client.GetStringAsync("/Warehouse"))},{"Rows[0].RecipientName","Blocked"}}));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode); Assert.Contains("Set your warehouse first",await response.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task Guided_browser_journey_covers_warehouse_batch_assignment_and_map_focus()
    {
        var unified = Environment.GetEnvironmentVariable("DALAN_UNIFIED_BROWSER") == "1";
        if(!unified && Environment.GetEnvironmentVariable("DALAN_WORKFLOW_BROWSER")!="1") return;
        var browserFixture = new SqlServerFixture();
        await browserFixture.InitializeAsync();
        try
        {
            using var factory=new WebApplicationFactory<WebAssemblyMarker>().WithWebHostBuilder(b=>b.UseEnvironment("Testing").UseSetting("ConnectionStrings:Dalan",browserFixture.ConnectionString)); var staff=await Staff(factory);
            var vehicle=Vehicle.Register("FLOW-"+Guid.NewGuid().ToString("N")[..10],"Van",100);
            var driver=Driver.Register("Workflow driver","09123456789",Guid.NewGuid().ToString("N"),DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1)); driver.AssignVehicle(vehicle.Id);
            await using(var db=browserFixture.CreateContext()){db.AddRange(vehicle,driver);await db.SaveChangesAsync();}
            if (unified)
            {
                await using var db = browserFixture.CreateContext();
                for (var i = 0; i < 8; i++)
                    db.Vehicles.Add(Vehicle.Register("READY-"+Guid.NewGuid().ToString("N")[..10],"Van",100));
                await db.SaveChangesAsync();
            }
            var info=new ProcessStartInfo("node"){RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
            info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,unified ? "../../../../../tests/browser/unified-dispatch.e2e.cjs" : "../../../../../tests/browser/delivery-workflow.e2e.cjs"));
            using var process=Process.Start(info)!;
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new{email=staff.Email,password=staff.Password,connection=browserFixture.ConnectionString,vehicleId=vehicle.Id,driverId=driver.Id})); process.StandardInput.Close();
            var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();Assert.True(process.ExitCode==0,await output+await error);
            await using var verify=browserFixture.CreateContext();Assert.Equal(2,await verify.Deliveries.CountAsync(x=>x.RecipientName.StartsWith("Guided browser ")));
            using var scope=factory.Services.CreateScope();
            var remaining=await scope.ServiceProvider.GetRequiredService<Dalan.Application.Deliveries.IDeliveryService>().ListForPlanningAsync();
            Assert.DoesNotContain(remaining,x=>x.RecipientName.StartsWith("Guided browser "));
        }
        finally { await browserFixture.DisposeAsync(); }
    }
}
