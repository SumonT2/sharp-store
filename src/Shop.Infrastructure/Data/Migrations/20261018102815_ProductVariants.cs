using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shop.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProductVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ListAvailability",
                table: "Products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ListLeadTimeDays",
                table: "Products",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ListOfferEndsAt",
                table: "Products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ListOptionCount",
                table: "Products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "ListPrice",
                table: "Products",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ListPriceMax",
                table: "Products",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VariantLabel",
                table: "Products",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VariantOfId",
                table: "Products",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_ListPrice",
                table: "Products",
                column: "ListPrice");

            migrationBuilder.CreateIndex(
                name: "IX_Products_VariantOfId",
                table: "Products",
                column: "VariantOfId");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Products_VariantOfId",
                table: "Products",
                column: "VariantOfId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_Products_VariantOfId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_ListPrice",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_VariantOfId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ListAvailability",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ListLeadTimeDays",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ListOfferEndsAt",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ListOptionCount",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ListPrice",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ListPriceMax",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "VariantLabel",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "VariantOfId",
                table: "Products");
        }
    }
}
