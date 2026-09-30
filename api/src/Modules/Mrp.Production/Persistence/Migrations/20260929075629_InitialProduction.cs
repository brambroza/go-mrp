using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mrp.Production.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialProduction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "production");

            migrationBuilder.CreateTable(
                name: "bom_versions",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    batch_size = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: true),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bom_versions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "demands",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reference_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_demands", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mrp_runs",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    run_date = table.Column<DateOnly>(type: "date", nullable: false),
                    horizon_days = table.Column<int>(type: "integer", nullable: false),
                    run_by = table.Column<Guid>(type: "uuid", nullable: false),
                    item_count = table.Column<int>(type: "integer", nullable: false),
                    planned_order_count = table.Column<int>(type: "integer", nullable: false),
                    exception_count = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mrp_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bom_lines",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bom_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    component_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    conversion_factor = table.Column<decimal>(type: "numeric(18,9)", precision: 18, scale: 9, nullable: false),
                    stock_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    loss_percent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    remark = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bom_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_bom_lines_boms_bom_version_id",
                        column: x => x.bom_version_id,
                        principalSchema: "production",
                        principalTable: "bom_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_orders",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    produced_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status_before_hold = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    bom_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_work_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    demand_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_work_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_work_orders_bom_versions_bom_version_id",
                        column: x => x.bom_version_id,
                        principalSchema: "production",
                        principalTable: "bom_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_orders_demands_demand_id",
                        column: x => x.demand_id,
                        principalSchema: "production",
                        principalTable: "demands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_orders_work_orders_parent_work_order_id",
                        column: x => x.parent_work_order_id,
                        principalSchema: "production",
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mrp_exceptions",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mrp_exceptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_mrp_exceptions_mrp_runs_run_id",
                        column: x => x.run_id,
                        principalSchema: "production",
                        principalTable: "mrp_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mrp_planned_orders",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    release_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    is_late = table.Column<bool>(type: "boolean", nullable: false),
                    pegging = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    converted_document_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    converted_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    converted_document_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mrp_planned_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_mrp_planned_orders_mrp_runs_run_id",
                        column: x => x.run_id,
                        principalSchema: "production",
                        principalTable: "mrp_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mrp_requirements",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    on_hand = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    safety_stock = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    gross = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    scheduled_receipts = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    net = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    planned = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mrp_requirements", x => x.id);
                    table.ForeignKey(
                        name: "fk_mrp_requirements_mrp_runs_run_id",
                        column: x => x.run_id,
                        principalSchema: "production",
                        principalTable: "mrp_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_order_materials",
                schema: "production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    component_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    required_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    issued_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    is_made_in_house = table.Column<bool>(type: "boolean", nullable: false),
                    child_work_order_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_order_materials", x => x.id);
                    table.ForeignKey(
                        name: "fk_work_order_materials_work_orders_work_order_id",
                        column: x => x.work_order_id,
                        principalSchema: "production",
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bom_lines_bom_version_id_line_no",
                schema: "production",
                table: "bom_lines",
                columns: new[] { "bom_version_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bom_lines_tenant_id",
                schema: "production",
                table: "bom_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_bom_lines_tenant_id_component_item_id",
                schema: "production",
                table: "bom_lines",
                columns: new[] { "tenant_id", "component_item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bom_versions_tenant_id",
                schema: "production",
                table: "bom_versions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_bom_versions_tenant_id_item_id_version_no",
                schema: "production",
                table: "bom_versions",
                columns: new[] { "tenant_id", "item_id", "version_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_bom_versions_active_item",
                schema: "production",
                table: "bom_versions",
                columns: new[] { "tenant_id", "item_id" },
                unique: true,
                filter: "status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_demands_tenant_id",
                schema: "production",
                table: "demands",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_demands_tenant_id_item_id",
                schema: "production",
                table: "demands",
                columns: new[] { "tenant_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_demands_tenant_id_status_due_date",
                schema: "production",
                table: "demands",
                columns: new[] { "tenant_id", "status", "due_date" });

            migrationBuilder.CreateIndex(
                name: "ix_mrp_exceptions_run_id",
                schema: "production",
                table: "mrp_exceptions",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_exceptions_tenant_id",
                schema: "production",
                table: "mrp_exceptions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_planned_orders_run_id_status",
                schema: "production",
                table: "mrp_planned_orders",
                columns: new[] { "run_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_mrp_planned_orders_tenant_id",
                schema: "production",
                table: "mrp_planned_orders",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_requirements_run_id_item_id",
                schema: "production",
                table: "mrp_requirements",
                columns: new[] { "run_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_mrp_requirements_tenant_id",
                schema: "production",
                table: "mrp_requirements",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_runs_tenant_id",
                schema: "production",
                table: "mrp_runs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_mrp_runs_tenant_id_document_no",
                schema: "production",
                table: "mrp_runs",
                columns: new[] { "tenant_id", "document_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_order_materials_tenant_id",
                schema: "production",
                table: "work_order_materials",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_materials_tenant_id_component_item_id",
                schema: "production",
                table: "work_order_materials",
                columns: new[] { "tenant_id", "component_item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_work_order_materials_work_order_id_line_no",
                schema: "production",
                table: "work_order_materials",
                columns: new[] { "work_order_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_bom_version_id",
                schema: "production",
                table: "work_orders",
                column: "bom_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_demand_id",
                schema: "production",
                table: "work_orders",
                column: "demand_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_parent_work_order_id",
                schema: "production",
                table: "work_orders",
                column: "parent_work_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_tenant_id",
                schema: "production",
                table: "work_orders",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_tenant_id_demand_id",
                schema: "production",
                table: "work_orders",
                columns: new[] { "tenant_id", "demand_id" },
                filter: "demand_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_tenant_id_document_no",
                schema: "production",
                table: "work_orders",
                columns: new[] { "tenant_id", "document_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_tenant_id_item_id",
                schema: "production",
                table: "work_orders",
                columns: new[] { "tenant_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_tenant_id_status_due_date",
                schema: "production",
                table: "work_orders",
                columns: new[] { "tenant_id", "status", "due_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bom_lines",
                schema: "production");

            migrationBuilder.DropTable(
                name: "mrp_exceptions",
                schema: "production");

            migrationBuilder.DropTable(
                name: "mrp_planned_orders",
                schema: "production");

            migrationBuilder.DropTable(
                name: "mrp_requirements",
                schema: "production");

            migrationBuilder.DropTable(
                name: "work_order_materials",
                schema: "production");

            migrationBuilder.DropTable(
                name: "mrp_runs",
                schema: "production");

            migrationBuilder.DropTable(
                name: "work_orders",
                schema: "production");

            migrationBuilder.DropTable(
                name: "bom_versions",
                schema: "production");

            migrationBuilder.DropTable(
                name: "demands",
                schema: "production");
        }
    }
}
