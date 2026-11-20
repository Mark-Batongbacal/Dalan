using Dalan.Application.Deliveries;
using Dalan.Application.Dispatch;
using Dalan.Contracts.Dispatch;
using Dalan.Domain.Deliveries;
using Xunit;

namespace Dalan.UnitTests;

public sealed class DeliveryBatchTests
{
    [Fact]
    public async Task Invalid_second_delivery_never_persists_the_valid_first_delivery()
    {
        var repo=new BatchRepository();
        var result=await new DeliveryBatchService(repo,TimeProvider.System).CreateAsync([Command("One"),Command(" ")]);
        Assert.False(result.Created); Assert.Empty(repo.Items); Assert.Contains("Delivery 2",result.Error);
    }
    [Fact]
    public async Task Valid_batch_has_distinct_server_generated_references_and_one_repository_write()
    {
        var repo=new BatchRepository();
        var result=await new DeliveryBatchService(repo,TimeProvider.System).CreateAsync([Command("One"),Command("Two")]);
        Assert.True(result.Created); Assert.Equal(2,result.DeliveryIds.Distinct().Count()); Assert.Equal(1,repo.Writes);
    }
    [Fact]
    public async Task Oversized_batch_is_rejected_before_persistence()
    {
        var repo=new BatchRepository();
        Assert.False((await new DeliveryBatchService(repo,TimeProvider.System).CreateAsync(Enumerable.Range(0,16).Select(i=>Command(i.ToString())).ToArray())).Created);
        Assert.Equal(0,repo.Writes);
    }
    [Theory]
    [InlineData(double.NaN,121)]
    [InlineData(14.6,double.PositiveInfinity)]
    public async Task Warehouse_rejects_non_finite_locations(double latitude,double longitude)
    {
        var repo=new WarehouseRepository();
        await Assert.ThrowsAsync<ArgumentException>(()=>new WarehousePreferenceService(repo,TimeProvider.System).SaveAsync("owner",new("Depot",latitude,longitude)));
        Assert.Null(repo.Saved);
    }
    static CreateDeliveryCommand Command(string name)=>new(name,"Warehouse",14.6,121.03,"Destination",14.61,121.04,2);
    sealed class BatchRepository : IDeliveryBatchRepository
    {
        public IReadOnlyList<Delivery> Items=[]; public int Writes;
        public Task AddRangeAsync(IReadOnlyList<Delivery> items,CancellationToken token=default){Items=items;Writes++;return Task.CompletedTask;}
    }
    sealed class WarehouseRepository : IWarehousePreferenceRepository
    {
        public WarehousePreference? Saved;
        public Task<WarehousePreference?> GetAsync(string id,CancellationToken token=default)=>Task.FromResult(Saved);
        public Task SaveAsync(string id,WarehousePreference value,CancellationToken token=default){Saved=value;return Task.CompletedTask;}
    }
}
