using System.Net;
using System.Net.Http.Json;
using Mrp.Inventory.Application;
using Mrp.Masters.Application;

namespace Mrp.Integration.Tests.Infrastructure;

/// <summary>Master data of a test tenant, created through the public API.</summary>
public sealed class Factory
{
    private readonly HttpClient _client;
    private int _sequence;

    private Factory(HttpClient client)
    {
        _client = client;
    }

    /// <summary>Unit "KG".</summary>
    public CodedDto Kg { get; private set; } = null!;

    /// <summary>Unit "G".</summary>
    public CodedDto Gram { get; private set; } = null!;

    /// <summary>Unit "PCS".</summary>
    public CodedDto Piece { get; private set; } = null!;

    /// <summary>Unit "BOX".</summary>
    public CodedDto Box { get; private set; } = null!;

    /// <summary>Main warehouse.</summary>
    public WarehouseDto Main { get; private set; } = null!;

    /// <summary>Production warehouse.</summary>
    public WarehouseDto Line { get; private set; } = null!;

    /// <summary>Supplier.</summary>
    public SupplierDto Supplier { get; private set; } = null!;

    /// <summary>Creates units, warehouses and a supplier for the tenant.</summary>
    public static async Task<Factory> CreateAsync(HttpClient client)
    {
        var factory = new Factory(client);
        factory.Kg = await factory.PostAsync<CodedDto>("/api/v1/masters/units", new { code = "KG", name = "กิโลกรัม" });
        factory.Gram = await factory.PostAsync<CodedDto>("/api/v1/masters/units", new { code = "G", name = "กรัม" });
        factory.Piece = await factory.PostAsync<CodedDto>("/api/v1/masters/units", new { code = "PCS", name = "ชิ้น" });
        factory.Box = await factory.PostAsync<CodedDto>("/api/v1/masters/units", new { code = "BOX", name = "กล่อง" });
        await factory.PostAsync<UnitConversionDto>("/api/v1/masters/unit-conversions", new { fromUnitId = factory.Kg.Id, toUnitId = factory.Gram.Id, factor = 1000 });
        factory.Main = await factory.PostAsync<WarehouseDto>("/api/v1/masters/warehouses", new { code = "WH-MAIN", name = "คลังหลัก", warehouseType = "General" });
        factory.Line = await factory.PostAsync<WarehouseDto>("/api/v1/masters/warehouses", new { code = "WH-LINE", name = "คลังหน้าไลน์", warehouseType = "Production" });
        factory.Supplier = await factory.PostAsync<SupplierDto>("/api/v1/masters/suppliers", new { code = "SUP-001", name = "บริษัท ซัพพลาย จำกัด", currency = "THB", creditDays = 30 });
        return factory;
    }

    /// <summary>Creates a raw material kept in kilograms.</summary>
    public Task<ItemDto> RawMaterialAsync(decimal standardCost = 100m, int? shelfLifeDays = null, decimal safetyStock = 0m, int leadTimeDays = 0) =>
        PostAsync<ItemDto>("/api/v1/masters/items", new
        {
            code = $"RM-{Interlocked.Increment(ref _sequence):000}",
            name = "วัตถุดิบทดสอบ",
            itemType = "RawMaterial",
            supplyType = "Buy",
            stockUnitId = Kg.Id,
            standardCost,
            shelfLifeDays,
            safetyStock,
            leadTimeDays,
        });

    /// <summary>Creates a packaging item kept in pieces, purchased in boxes of 48.</summary>
    public async Task<ItemDto> PackagingAsync(decimal piecesPerBox = 48m)
    {
        var item = await PostAsync<ItemDto>("/api/v1/masters/items", new
        {
            code = $"PK-{Interlocked.Increment(ref _sequence):000}",
            name = "ขวดทดสอบ",
            itemType = "Packaging",
            supplyType = "Buy",
            stockUnitId = Piece.Id,
            purchaseUnitId = Box.Id,
            standardCost = 2.5m,
        });
        await PostAsync<UnitConversionDto>("/api/v1/masters/unit-conversions", new { itemId = item.Id, fromUnitId = Box.Id, toUnitId = Piece.Id, factor = piecesPerBox });
        return item;
    }

    /// <summary>Creates and posts a goods receipt with one line; returns the posted document.</summary>
    public async Task<StockDocumentDto> ReceiveAsync(Guid itemId, decimal quantity, DateOnly date, decimal unitCost = 100m, Guid? unitId = null, string? lotNo = null, Guid? warehouseId = null, DateOnly? expiryDate = null)
    {
        var draft = await PostAsync<StockDocumentDto>("/api/v1/inventory/documents", new
        {
            documentType = "Receipt",
            documentDate = date,
            warehouseId = warehouseId ?? Main.Id,
            lines = new[] { new { itemId, quantity, unitId, unitCost, lotNo, expiryDate } },
        });
        return await PostAsync<StockDocumentDto>($"/api/v1/inventory/documents/{draft.Id}/post", new { });
    }

    /// <summary>Creates a draft warehouse document.</summary>
    public Task<HttpResponseMessage> DraftAsync(string documentType, DateOnly date, object[] lines, Guid? warehouseId = null, Guid? toWarehouseId = null, Guid? supplierId = null) =>
        _client.PostAsJsonAsync("/api/v1/inventory/documents", new
        {
            documentType,
            documentDate = date,
            warehouseId = warehouseId ?? Main.Id,
            toWarehouseId,
            supplierId,
            lines,
        });

    /// <summary>Posts JSON and returns the typed response, failing on any non-success status.</summary>
    public async Task<T> PostAsync<T>(string path, object body)
    {
        var response = await _client.PostAsJsonAsync(path, body);
        await response.ShouldBeAsync(HttpStatusCode.OK);
        return await response.ReadAsync<T>();
    }

    /// <summary>Gets JSON and returns the typed response.</summary>
    public async Task<T> GetAsync<T>(string path)
    {
        var response = await _client.GetAsync(path);
        await response.ShouldBeAsync(HttpStatusCode.OK);
        return await response.ReadAsync<T>();
    }
}
