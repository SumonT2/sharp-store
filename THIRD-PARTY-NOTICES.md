# Third-party software

This project uses the following open-source packages. All of them have permissive licenses that allow
commercial use, modification and closed-source distribution. Keep this file with the project.

| Package | License | Used for |
|---|---|---|
| .NET runtime, ASP.NET Core, Entity Framework Core, Microsoft.Extensions.Hosting.WindowsServices (Microsoft) | MIT | Web framework, database access, running as a Windows service |
| Npgsql / Npgsql.EntityFrameworkCore.PostgreSQL | PostgreSQL License (MIT-like) | PostgreSQL driver |
| CsvHelper | MS-PL or Apache-2.0 (dual) | Reading CSV files |
| AngleSharp | MIT | Reading prices from HTML pages |
| xUnit, coverlet, Microsoft.NET.Test.Sdk | Apache-2.0 / MIT | Tests only (not deployed) |
| PostgreSQL (database server) | PostgreSQL License | Database |
| Manrope font (The Manrope Project Authors) | SIL Open Font License 1.1 | Body text (`wwwroot/fonts/manrope.woff2`) |
| Space Grotesk font (The Space Grotesk Project Authors) | SIL Open Font License 1.1 | Headings and prices (`wwwroot/fonts/space-grotesk.woff2`) |

The font licences are in `src/Shop.Web/wwwroot/fonts/`. The OFL allows using, embedding and redistributing the fonts,
including commercially; it only forbids selling the font files on their own.

No third-party CSS frameworks, icon sets or images are included. The design, stylesheet and icons were written for
this project. The demo product photos in `wwwroot/img/demo/` are original renders made by
`tools/demo-images/render.js`; the brands and products in them are fictional.

Regenerate the package list at any time with:

    dotnet list package --include-transitive
