# Shop

An ASP.NET Core (.NET 10) online store for selling products sourced from several suppliers:

- **Supplier offers.** Each product can have offers from several suppliers: your own warehouse, local importers, an overseas partner and scraped websites. Each offer has its own cost, currency, stock, delivery time and optional validity window.
- **Automatic pricing.** The selling price is calculated from the best available offer using pricing rules (markup %, fixed markup, minimum profit, rounding) and exchange rates. In-stock offers win over pre-orders. When a time-limited offer ends, the store switches to the next best offer automatically.
- **Pre-orders.** Time-limited overseas prices (e.g. "this price until 31 Oct, delivery ~15 days") are shown with a countdown and an estimated delivery date.
- **CSV imports.** One import is for catalog data (products, categories, brands, specifications); the other updates stock and prices per supplier. Each supplier can have its own column names.
- **Price scraping.** For web-source suppliers, the scraper reads only the price and stock status, respects robots.txt and waits between requests.
- **Storefront.** It has faceted filtering by any specification (with counts), sorting, search, detailed spec tables, a cart and cash-on-delivery checkout.
- **Admin.** The admin area covers products, offers, suppliers, pricing rules, exchange rates, CSV imports, orders and jobs.

## Project layout

```
src/Shop.Core            Entities and the pricing engine (no dependencies)
src/Shop.Infrastructure  EF Core (PostgreSQL), CSV import, catalog search, scraping, orders
src/Shop.Web             Storefront (MVC + Razor) and admin area
tests/Shop.Tests         Unit and import tests (xUnit, SQLite in-memory)
deploy/                  Docker Compose, nginx config, env example
```

## Run locally

Requirements: .NET 10 SDK and PostgreSQL 14+.

```bash
# Create a database user (development values; match appsettings.Development.json)
sudo -u postgres psql -c "CREATE USER shop WITH PASSWORD 'shop' CREATEDB;"
sudo -u postgres createdb -O shop shop

cd src/Shop.Web
dotnet run
```

In Development the app applies database migrations, creates an admin user (`admin@example.com` / `ChangeMe123!`) and adds fictional demo products. Visit `/` for the store and `/admin` for the admin area. Change the password right away.

Run tests with `dotnet test`.

## Configuration (`appsettings.json` or environment variables)

| Setting | Meaning |
|---|---|
| `ConnectionStrings:Shop` | PostgreSQL connection string |
| `Shop:Name`, `Shop:BaseCurrency`, `Shop:CurrencySymbol` | Store name and selling currency |
| `Shop:TimeZone` | Time zone for offer dates in CSVs and on the site, e.g. `Asia/Dhaka` |
| `Shop:InStockMaxLeadDays` | Offers delivering within this many days count as "in stock"; slower ones are pre-orders |
| `Shop:ShippingFee`, `Shop:FreeShippingOver` | Flat delivery fee and free-delivery threshold |
| `Admin:Email`, `Admin:Password` | Creates the first admin when none exists |
| `Jobs:RepriceIntervalMinutes` | How often prices are recalculated (so offer windows open and close on time) |
| `Jobs:ScrapingEnabled`, `Jobs:ScrapeIntervalHours` | Automatic scraping schedule |
| `Scraping:UserAgent` | Put your real contact address here |
| `Seed:DemoData` | Add demo data to an empty database |

Environment variables use `__` instead of `:`. For example, `Shop__TimeZone=Asia/Dhaka`.

## Typical workflow

1. **Suppliers.** Add each supplier with its currency, default delivery days and extra costs (shipping or duty as a % plus a fixed amount per unit).
2. **Pricing.** Add a default rule (e.g. 18% markup, rounding *Ends99*) and exchange rates for foreign currencies.
3. **Catalog CSV.** Import your products and specifications. Download the template from *CSV import*.
4. **Stock & price CSV.** Import each supplier's list. Tick *full list* to set missing items to stock 0. For the overseas partner, include `valid_from` and `valid_to` for the offer period, and use `*` as stock when quantity is unlimited.
5. **Done.** Prices update immediately. They're also recalculated every few minutes, so when the China offer window closes, the product switches to the next best offer or becomes out of stock.

## Deploy to a VPS (Ubuntu)

```bash
# On the server, with Docker installed
git clone <your repo> shop && cd shop
cp deploy/.env.example deploy/.env   # edit the values
docker compose -f deploy/docker-compose.yml up -d --build
```

Then install nginx with `deploy/nginx.conf` and get a free HTTPS certificate with `certbot --nginx`. Back up the `pgdata` volume with `pg_dump` regularly.

## Copyright and licensing notes

- **Code.** All dependencies are permissively licensed (see `THIRD-PARTY-NOTICES.md`). The design, CSS and icons are original and use no third-party assets.
- **Product content.** Only use product photos and descriptions that you created yourself or that the manufacturer or importer allows you to use. Don't copy them from other shops.
- **Scraping.** The scraper only collects prices and stock status. Only scrape sites whose terms of service allow it or whose owner has agreed; a data feed from the supplier is better when available.
- **Brand names.** Using brand and product names to describe genuine products you sell is normal. Don't use other companies' logos as your own branding.

This is general guidance, not legal advice.
