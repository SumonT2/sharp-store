# TechBhai

An ASP.NET Core (.NET 10) online store for mechanical keyboards, mice and accessories in Bangladesh. Prices are in ৳ BDT and shown as 1,24,990; offer dates use Dhaka time; checkout has inside/outside Dhaka delivery charges, cash on delivery and Bangladeshi mobile number checks. It sells products sourced from several suppliers:

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
| `Shop:Name`, `Shop:Tagline` | Store name and footer line |
| `Shop:BaseCurrency`, `Shop:CurrencySymbol`, `Shop:PriceDecimals`, `Shop:NumberCulture` | Selling currency and price format (BDT, ৳, 0, `en-IN`) |
| `Shop:TimeZone` | Time zone for offer dates in CSVs and on the site (`Asia/Dhaka`) |
| `Shop:InStockMaxLeadDays` | Offers delivering within this many days count as "in stock"; slower ones are pre-orders |
| `Shop:DeliveryZones`, `Shop:FreeShippingOver` | Delivery areas with fees (inside Dhaka ৳70, outside ৳130) and the free-delivery threshold |
| `Shop:PhonePattern`, `Shop:PhoneHint` | Phone number check at checkout (Bangladeshi mobile numbers) |
| `Shop:Hotline`, `Shop:SupportHours`, `Shop:Address` | Shown in the top bar, footer and on phones as a call button; leave empty to hide |
| `Shop:Accent` | Default accent colour: `ocean`, `emerald`, `teal`, `violet`, `amber`, `raspberry` or `coral`. Visitors can pick their own in the display settings (the half-circle icon) |
| `Shop:Hero` | Home page banner: `Eyebrow`, `Title` (`\n` for a new line), `Text`, `Url`, `LinkText`, or `ProductSku` to advertise one product with its live price. `Background` picks a preset: `accent`, `waves`, `geometric`, `swoosh` (these follow the visitor's colour), `watercolor`, `halftone`, `sky`, `mint`, `peach`, `lavender`, `aurora`, `sunset`, `streaks`, `network`, `midnight` (while developing, add `?hero-bg=waves` to the home page address to preview one); `ImageUrl` is then a product cut-out with a transparent background, shown on the right. With `Background` = `photo`, `ImageUrl` is a wide photo filling the banner (subject on the right) and `DarkImageUrl` an optional darker one for the dark theme |
| `Shop:Promo` | Optional small banner beside the hero (`Eyebrow`, `Title`, `Url`, `LinkText`, `Background`); the other one advertises pre-order deals automatically |
| `Shop:DealTileBackground`, `Shop:DealsBackground` | Background presets for the pre-order tile and the pre-order deals band (same names as `Shop:Hero:Background`); empty keeps them plain. The development settings show a demo combination |
| `Admin:Email`, `Admin:Password` | Creates the first admin when none exists |
| `Jobs:RepriceIntervalMinutes` | How often prices are recalculated (so offer windows open and close on time) |
| `Jobs:ScrapingEnabled`, `Jobs:ScrapeIntervalHours` | Automatic scraping schedule |
| `Scraping:UserAgent` | Put your real contact address here |
| `Seed:DemoData` | Add demo data to an empty database |

Environment variables use `__` instead of `:`. For example, `Shop__TimeZone=Asia/Dhaka`.

## Typical workflow

1. **Suppliers.** Add each supplier with its currency, default delivery days and extra costs (shipping or duty as a % plus a fixed amount per unit).
2. **Pricing.** Add a default rule (e.g. 18% markup, rounding *Ends99*) and exchange rates for foreign currencies.
3. **Catalog CSV.** Import your products and specifications. See [`docs/CSV-GUIDE.md`](docs/CSV-GUIDE.md) and the ready-made examples in [`samples/csv`](samples/csv).
4. **Stock & price CSV.** Import each supplier's list. Tick *full list* to set missing items to stock 0. For the overseas partner, include `valid_from` and `valid_to` for the offer period, and use `*` as stock when quantity is unlimited.
5. **Website suppliers.** For suppliers that publish prices online (e.g. UGREEN from a distributor), upload a list of product links and let the scraper fill in prices. A dealer discount such as 7% is set on the supplier as extra cost `-7`. See the CSV guide, section 3.
6. **Done.** Prices update immediately. They're also recalculated every few minutes, so when the China offer window closes, the product switches to the next best offer or becomes out of stock.

## Deploy to a VPS (Ubuntu)

```bash
# On the server, with Docker installed
git clone <your repo> shop && cd shop
cp deploy/.env.example deploy/.env   # edit the values
docker compose -f deploy/docker-compose.yml up -d --build
```

Then install nginx with `deploy/nginx.conf` and get a free HTTPS certificate with `certbot --nginx`. Back up the `pgdata` volume with `pg_dump` regularly.

## Copyright and licensing notes

- **Code.** All dependencies are permissively licensed (see `THIRD-PARTY-NOTICES.md`). The design, CSS and icons are original. The two fonts are under the SIL Open Font License, which allows commercial use.
- **Demo photos.** The pictures in `wwwroot/img/demo/` are original renders of fictional products, made for this project. Replace them with photos of the real products you sell.
- **Product content.** Only use product photos and descriptions that you created yourself or that the manufacturer or importer allows you to use. Don't copy them from other shops.
- **Scraping.** The scraper only collects prices and stock status. Only scrape sites whose terms of service allow it or whose owner has agreed; a data feed from the supplier is better when available.
- **Brand names.** Using brand and product names to describe genuine products you sell is normal. Don't use other companies' logos as your own branding.

This is general guidance, not legal advice.
