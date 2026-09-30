using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mrp.Inventory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.CreateTable(
                name: "lots",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    supplier_lot = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    mfg_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    qc_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    qc_remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "period_balances",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_period_balances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "periods",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reopened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reopened_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reopen_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_periods", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_documents",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    document_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    document_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_warehouse_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reference_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reference_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    posted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "balances",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_balances", x => x.id);
                    table.ForeignKey(
                        name: "fk_balances_lots_lot_id",
                        column: x => x.lot_id,
                        principalSchema: "inventory",
                        principalTable: "lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "movements",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    posting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    movement_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    document_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    document_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    po_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reverses_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movements", x => x.id);
                    table.ForeignKey(
                        name: "fk_movements_lots_lot_id",
                        column: x => x.lot_id,
                        principalSchema: "inventory",
                        principalTable: "lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_document_allocations",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_document_allocations", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_document_allocations_lots_lot_id",
                        column: x => x.lot_id,
                        principalSchema: "inventory",
                        principalTable: "lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_document_allocations_stock_documents_document_id",
                        column: x => x.document_id,
                        principalSchema: "inventory",
                        principalTable: "stock_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_document_lines",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    conversion_factor = table.Column<decimal>(type: "numeric(18,9)", precision: 18, scale: 9, nullable: false),
                    stock_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lot_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    supplier_lot = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    mfg_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    po_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    system_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    remark = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_lot_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_document_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_document_lines_stock_documents_document_id",
                        column: x => x.document_id,
                        principalSchema: "inventory",
                        principalTable: "stock_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_balances_lot_id",
                schema: "inventory",
                table: "balances",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_balances_tenant_id",
                schema: "inventory",
                table: "balances",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_balances_tenant_id_item_id_lot_id_warehouse_id_location_id",
                schema: "inventory",
                table: "balances",
                columns: new[] { "tenant_id", "item_id", "lot_id", "warehouse_id", "location_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_balances_tenant_id_warehouse_id_item_id",
                schema: "inventory",
                table: "balances",
                columns: new[] { "tenant_id", "warehouse_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_lots_tenant_id",
                schema: "inventory",
                table: "lots",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_lots_tenant_id_item_id_received_at",
                schema: "inventory",
                table: "lots",
                columns: new[] { "tenant_id", "item_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_lots_tenant_id_lot_no",
                schema: "inventory",
                table: "lots",
                columns: new[] { "tenant_id", "lot_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_movements_lot_id",
                schema: "inventory",
                table: "movements",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_movements_seq",
                schema: "inventory",
                table: "movements",
                column: "seq",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_movements_tenant_id",
                schema: "inventory",
                table: "movements",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_movements_tenant_id_document_id",
                schema: "inventory",
                table: "movements",
                columns: new[] { "tenant_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_movements_tenant_id_item_id_posting_date_seq",
                schema: "inventory",
                table: "movements",
                columns: new[] { "tenant_id", "item_id", "posting_date", "seq" });

            migrationBuilder.CreateIndex(
                name: "ix_movements_tenant_id_lot_id",
                schema: "inventory",
                table: "movements",
                columns: new[] { "tenant_id", "lot_id" });

            migrationBuilder.CreateIndex(
                name: "ix_movements_tenant_id_po_line_id",
                schema: "inventory",
                table: "movements",
                columns: new[] { "tenant_id", "po_line_id" },
                filter: "po_line_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_period_balances_tenant_id",
                schema: "inventory",
                table: "period_balances",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_period_balances_tenant_id_year_month_item_id",
                schema: "inventory",
                table: "period_balances",
                columns: new[] { "tenant_id", "year", "month", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_periods_tenant_id",
                schema: "inventory",
                table: "periods",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_periods_tenant_id_year_month",
                schema: "inventory",
                table: "periods",
                columns: new[] { "tenant_id", "year", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_document_allocations_document_id",
                schema: "inventory",
                table: "stock_document_allocations",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_document_allocations_line_id",
                schema: "inventory",
                table: "stock_document_allocations",
                column: "line_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_document_allocations_lot_id",
                schema: "inventory",
                table: "stock_document_allocations",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_document_allocations_tenant_id",
                schema: "inventory",
                table: "stock_document_allocations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_document_lines_document_id_line_no",
                schema: "inventory",
                table: "stock_document_lines",
                columns: new[] { "document_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_document_lines_tenant_id",
                schema: "inventory",
                table: "stock_document_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_document_lines_tenant_id_po_line_id",
                schema: "inventory",
                table: "stock_document_lines",
                columns: new[] { "tenant_id", "po_line_id" },
                filter: "po_line_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_documents_tenant_id",
                schema: "inventory",
                table: "stock_documents",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_documents_tenant_id_document_no",
                schema: "inventory",
                table: "stock_documents",
                columns: new[] { "tenant_id", "document_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_documents_tenant_id_document_type_status_document_date",
                schema: "inventory",
                table: "stock_documents",
                columns: new[] { "tenant_id", "document_type", "status", "document_date" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_documents_tenant_id_reference_id",
                schema: "inventory",
                table: "stock_documents",
                columns: new[] { "tenant_id", "reference_id" },
                filter: "reference_id IS NOT NULL");

            migrationBuilder.Sql(LedgerGuardSql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(LedgerGuardSql.Down);

            migrationBuilder.DropTable(
                name: "balances",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "movements",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "period_balances",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "periods",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_document_allocations",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_document_lines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "lots",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_documents",
                schema: "inventory");
        }
    }
}
