using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mrp.Purchasing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPurchasing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "purchasing");

            migrationBuilder.CreateTable(
                name: "purchase_orders",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    document_date = table.Column<DateOnly>(type: "date", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    exchange_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    vat_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    credit_days = table.Column<int>(type: "integer", nullable: false),
                    delivery_date = table.Column<DateOnly>(type: "date", nullable: true),
                    remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("pk_purchase_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_requests",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    document_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    required_date = table.Column<DateOnly>(type: "date", nullable: true),
                    remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_purchase_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_request_lines",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    conversion_factor = table.Column<decimal>(type: "numeric(18,9)", precision: 18, scale: 9, nullable: false),
                    stock_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    required_date = table.Column<DateOnly>(type: "date", nullable: true),
                    suggested_supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    remark = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_request_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_request_lines_purchase_requests_request_id",
                        column: x => x.request_id,
                        principalSchema: "purchasing",
                        principalTable: "purchase_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_lines",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    conversion_factor = table.Column<decimal>(type: "numeric(18,9)", precision: 18, scale: 9, nullable: false),
                    stock_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    discount_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    net_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    delivery_date = table.Column<DateOnly>(type: "date", nullable: true),
                    pr_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    remark = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    received_stock_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    is_closed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_purchase_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "purchasing",
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_request_lines_pr_line_id",
                        column: x => x.pr_line_id,
                        principalSchema: "purchasing",
                        principalTable: "purchase_request_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_order_id_line_no",
                schema: "purchasing",
                table: "purchase_order_lines",
                columns: new[] { "order_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_pr_line_id",
                schema: "purchasing",
                table: "purchase_order_lines",
                column: "pr_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_tenant_id",
                schema: "purchasing",
                table: "purchase_order_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_tenant_id_item_id",
                schema: "purchasing",
                table: "purchase_order_lines",
                columns: new[] { "tenant_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_tenant_id_pr_line_id",
                schema: "purchasing",
                table: "purchase_order_lines",
                columns: new[] { "tenant_id", "pr_line_id" },
                filter: "pr_line_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_tenant_id",
                schema: "purchasing",
                table: "purchase_orders",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_tenant_id_document_no",
                schema: "purchasing",
                table: "purchase_orders",
                columns: new[] { "tenant_id", "document_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_tenant_id_status_document_date",
                schema: "purchasing",
                table: "purchase_orders",
                columns: new[] { "tenant_id", "status", "document_date" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_tenant_id_supplier_id",
                schema: "purchasing",
                table: "purchase_orders",
                columns: new[] { "tenant_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_request_lines_request_id_line_no",
                schema: "purchasing",
                table: "purchase_request_lines",
                columns: new[] { "request_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_request_lines_tenant_id",
                schema: "purchasing",
                table: "purchase_request_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_request_lines_tenant_id_item_id",
                schema: "purchasing",
                table: "purchase_request_lines",
                columns: new[] { "tenant_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_requests_tenant_id",
                schema: "purchasing",
                table: "purchase_requests",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_requests_tenant_id_document_no",
                schema: "purchasing",
                table: "purchase_requests",
                columns: new[] { "tenant_id", "document_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_requests_tenant_id_status_document_date",
                schema: "purchasing",
                table: "purchase_requests",
                columns: new[] { "tenant_id", "status", "document_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_order_lines",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "purchase_orders",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "purchase_request_lines",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "purchase_requests",
                schema: "purchasing");
        }
    }
}
