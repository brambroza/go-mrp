using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mrp.Masters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialMasters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "masters");

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "masters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_limit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    check_credit = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tax_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    branch_no = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    contact_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    credit_days = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "item_groups",
                schema: "masters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                schema: "masters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    vat_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tax_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    branch_no = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    contact_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    credit_days = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suppliers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "units",
                schema: "masters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_units", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "warehouses",
                schema: "masters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_warehouses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "items",
                schema: "masters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    supply_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    item_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stock_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    barcode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_lot_tracked = table.Column<bool>(type: "boolean", nullable: false),
                    shelf_life_days = table.Column<int>(type: "integer", nullable: true),
                    lead_time_days = table.Column<int>(type: "integer", nullable: false),
                    safety_stock = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    min_stock = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    max_stock = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    min_order_qty = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    order_multiple = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    standard_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_items_item_groups_item_group_id",
                        column: x => x.item_group_id,
                        principalSchema: "masters",
                        principalTable: "item_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_items_units_purchase_unit_id",
                        column: x => x.purchase_unit_id,
                        principalSchema: "masters",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_items_units_stock_unit_id",
                        column: x => x.stock_unit_id,
                        principalSchema: "masters",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "locations",
                schema: "masters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_locations", x => x.id);
                    table.ForeignKey(
                        name: "fk_locations_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalSchema: "masters",
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_prices",
                schema: "masters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    min_qty = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_prices", x => x.id);
                    table.ForeignKey(
                        name: "fk_supplier_prices_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "masters",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_supplier_prices_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalSchema: "masters",
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_supplier_prices_units_unit_id",
                        column: x => x.unit_id,
                        principalSchema: "masters",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "unit_conversions",
                schema: "masters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    from_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factor = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unit_conversions", x => x.id);
                    table.ForeignKey(
                        name: "fk_unit_conversions_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "masters",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_unit_conversions_units_from_unit_id",
                        column: x => x.from_unit_id,
                        principalSchema: "masters",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_unit_conversions_units_to_unit_id",
                        column: x => x.to_unit_id,
                        principalSchema: "masters",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customers_tenant_id",
                schema: "masters",
                table: "customers",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_customers_tenant_id_code",
                schema: "masters",
                table: "customers",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_item_groups_tenant_id",
                schema: "masters",
                table: "item_groups",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_groups_tenant_id_code",
                schema: "masters",
                table: "item_groups",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_items_item_group_id",
                schema: "masters",
                table: "items",
                column: "item_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_items_purchase_unit_id",
                schema: "masters",
                table: "items",
                column: "purchase_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_items_stock_unit_id",
                schema: "masters",
                table: "items",
                column: "stock_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_items_tenant_id",
                schema: "masters",
                table: "items",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_items_tenant_id_barcode",
                schema: "masters",
                table: "items",
                columns: new[] { "tenant_id", "barcode" },
                unique: true,
                filter: "barcode IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_items_tenant_id_code",
                schema: "masters",
                table: "items",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_items_tenant_id_item_type",
                schema: "masters",
                table: "items",
                columns: new[] { "tenant_id", "item_type" });

            migrationBuilder.CreateIndex(
                name: "ix_locations_tenant_id",
                schema: "masters",
                table: "locations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_locations_tenant_id_warehouse_id_code",
                schema: "masters",
                table: "locations",
                columns: new[] { "tenant_id", "warehouse_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_locations_warehouse_id",
                schema: "masters",
                table: "locations",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_prices_item_id",
                schema: "masters",
                table: "supplier_prices",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_prices_supplier_id",
                schema: "masters",
                table: "supplier_prices",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_prices_tenant_id",
                schema: "masters",
                table: "supplier_prices",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_prices_tenant_id_supplier_id_item_id",
                schema: "masters",
                table: "supplier_prices",
                columns: new[] { "tenant_id", "supplier_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_prices_unit_id",
                schema: "masters",
                table: "supplier_prices",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_tenant_id",
                schema: "masters",
                table: "suppliers",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_tenant_id_code",
                schema: "masters",
                table: "suppliers",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_unit_conversions_from_unit_id",
                schema: "masters",
                table: "unit_conversions",
                column: "from_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_unit_conversions_item_id",
                schema: "masters",
                table: "unit_conversions",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_unit_conversions_tenant_id",
                schema: "masters",
                table: "unit_conversions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_unit_conversions_tenant_id_item_id_from_unit_id_to_unit_id",
                schema: "masters",
                table: "unit_conversions",
                columns: new[] { "tenant_id", "item_id", "from_unit_id", "to_unit_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_unit_conversions_to_unit_id",
                schema: "masters",
                table: "unit_conversions",
                column: "to_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_units_tenant_id",
                schema: "masters",
                table: "units",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_units_tenant_id_code",
                schema: "masters",
                table: "units",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_warehouses_tenant_id",
                schema: "masters",
                table: "warehouses",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_warehouses_tenant_id_code",
                schema: "masters",
                table: "warehouses",
                columns: new[] { "tenant_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customers",
                schema: "masters");

            migrationBuilder.DropTable(
                name: "locations",
                schema: "masters");

            migrationBuilder.DropTable(
                name: "supplier_prices",
                schema: "masters");

            migrationBuilder.DropTable(
                name: "unit_conversions",
                schema: "masters");

            migrationBuilder.DropTable(
                name: "warehouses",
                schema: "masters");

            migrationBuilder.DropTable(
                name: "suppliers",
                schema: "masters");

            migrationBuilder.DropTable(
                name: "items",
                schema: "masters");

            migrationBuilder.DropTable(
                name: "item_groups",
                schema: "masters");

            migrationBuilder.DropTable(
                name: "units",
                schema: "masters");
        }
    }
}
