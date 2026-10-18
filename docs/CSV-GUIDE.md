# CSV guide

TechBhai uses two kinds of CSV file:

| File | What it changes | Where to upload |
|---|---|---|
| **Catalog** | Products: name, category, brand, descriptions, specifications | Admin → CSV import → *Catalog* |
| **Stock & price** | One supplier's cost, stock, delivery days and offer dates | Admin → CSV import → *Stock & prices* (choose the supplier) |

Ready-made examples are in [`samples/csv`](../samples/csv). Excel and Google Sheets can open and save these files. Choose **CSV UTF-8** when saving from Excel, so Bangla text and the ৳ sign survive.

## General rules

- The first row holds the column names. Column order doesn't matter, and unknown columns are ignored.
- Commas, semicolons and tabs are all accepted as separators.
- **Empty cell:** keeps the current value. You can send a file with only `sku` and `stock` to update stock alone.
- **`-` (a single dash):** clears the value.
- **Prices:** `24990`, `24,990`, `1,24,990` and `৳ 24,990` all work.
- **Dates:** use `yyyy-MM-dd` or `yyyy-MM-dd HH:mm`, in Bangladesh time. An end date without a time includes that whole day.
- If a row has a problem, it's skipped and listed on the import result page. All other rows are still imported.

## 1. Catalog CSV

Example: `samples/csv/1-catalog.csv`

| Column | Required | Notes |
|---|---|---|
| `sku` | yes | Your own product code. It must be unique and must never change; every other file refers to it. |
| `name` | for new products | |
| `category` | | A path like `Phones > Smartphones`. Missing categories are created. |
| `brand` | | |
| `short_description` | | Shown near the price. |
| `description` | | Plain text. Use a blank line between paragraphs. |
| `image_url` | | Link to the product photo. Only use photos you took or are allowed to use. |
| `published` | | `yes` or `no`. New products are published by default. |
| `compare_at_price` | | An optional "was" price, shown struck through. |
| `price_override` | | A fixed selling price that ignores the pricing rules. |
| `spec:Group > Name [unit]` | | One column per specification, e.g. `spec:Display > Screen Size [inch]`. The group and unit are optional. |

Specifications automatically become filters in the store. Keep the values consistent: write `8`, not `8GB` in one row and `8 GB` in another, because the unit is already in the column name.

## 2. Stock & price CSV (one supplier per file)

Examples: `2-importer-stock.csv` (local importer, prices in taka) and `4-china-price-list.csv` (China partner, prices in yuan, valid for a set period).

| Column | Notes |
|---|---|
| `sku` | Your SKU. Either this or `supplier_sku` is needed to find the product. |
| `supplier_sku` | The supplier's own code. Once saved, later files can use only this. |
| `cost` | The supplier's price to you. Required the first time a product is added for this supplier. |
| `currency` | `BDT`, `CNY`, `USD` and so on. Leave empty to use the supplier's currency. |
| `stock` | A number. `0` means sold out; `*` means unlimited or not tracked (order on demand). |
| `lead_time_days` | Delivery days. Leave empty to use the supplier's default. |
| `valid_from`, `valid_to` | The offer period. Outside it, the offer is ignored. |
| `source_url` | For website suppliers only: the page the scraper reads the price from. |
| `active` | `yes` or `no`. |
| `name` | Only used when *Create draft products* is ticked, for SKUs you don't have yet. |

Upload options:

- **"This is the supplier's full list":** any product of this supplier that is missing from the file is set to stock 0. Use this when the importer sends their complete stock list.
- **"Create draft products":** unknown items with a `name` become unpublished drafts for you to complete.

### When a supplier uses its own column names

You don't have to edit their file. Open *Suppliers → (supplier) → CSV column mapping* and map their headers to the standard names, one per line:

```
supplier_sku=Item Code
cost=Dealer Price
stock=Qty
```

After that, files like `samples/csv/3-importer-own-format.csv` import as they are.

## 3. Website suppliers (for example UGREEN from a distributor's site)

Use this when a supplier publishes its prices on its own website. Only do this if the supplier allows it; ideally get their permission or a price file. The scraper reads **only the price and stock status**. It never copies descriptions or photos.

1. **Add the supplier.** Go to *Suppliers → New supplier*, set *Type* to `WebSource` and *Currency* to `BDT`.
   - If the supplier gives you a dealer discount on its listed price, enter it as a negative *Extra cost %*. For a 7% discount, enter `-7`; the cost then becomes the listed price × 0.93.
2. **Add the products.** List them in a CSV like `samples/csv/5-website-supplier-links.csv`: `supplier_sku`, `name` and `source_url` (the product page). Upload it under *Stock & prices* for that supplier, with *Create draft products* ticked. No cost is needed yet.
3. **Fill in the prices.** Open one of the offers and click *Test price reading*.
   - If the price is found, go to *Jobs → Scrape now* to fill in all of them.
   - If not, set the *Price CSS selector* on the supplier (right-click the price in your browser → *Inspect* to find it) and test again.
4. **Publish.** Add your own category, specifications and photos to the drafts, then publish them.

Set the selling price with a pricing rule for that supplier. For example:

- **Selling 7% below the supplier's listed price:** extra cost `-7`, markup `0%`.
- **Selling at the listed price while keeping your 7% dealer margin:** extra cost `-7`, markup `7.53%`.

Turn on `Jobs:ScrapingEnabled` to refresh prices automatically every few hours.

## How the selling price is decided

For each product, the store looks at every supplier offer that is active, inside its date window and in stock (or unlimited). Then:

1. **Cost in taka.** The supplier's cost is converted with your exchange rate, then the supplier's extra costs are added (shipping or duty %, plus a fixed amount per unit).
2. **Pricing rule.** Your matching pricing rule adds the markup, enforces the minimum profit and rounds up (e.g. to the next ৳10).
3. **Best offer.** Offers that can deliver within 3 days count as **in stock** and always win. Slower offers, like the China partner, are sold as **pre-orders**. Within each group, the cheapest price wins.

Prices are recalculated after every import and edit, and every 10 minutes, so a China price stops being used the moment its `valid_to` passes.
