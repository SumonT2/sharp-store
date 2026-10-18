# Third-party software

This project uses the following open-source packages. All of them have permissive licenses that allow
commercial use, modification and closed-source distribution. Keep this file with the project.

| Package | License | Used for |
|---|---|---|
| .NET runtime, ASP.NET Core, Entity Framework Core (Microsoft) | MIT | Web framework, database access |
| Npgsql / Npgsql.EntityFrameworkCore.PostgreSQL | PostgreSQL License (MIT-like) | PostgreSQL driver |
| CsvHelper | MS-PL or Apache-2.0 (dual) | Reading CSV files |
| AngleSharp | MIT | Reading prices from HTML pages |
| xUnit, coverlet, Microsoft.NET.Test.Sdk | Apache-2.0 / MIT | Tests only (not deployed) |
| PostgreSQL (database server) | PostgreSQL License | Database |

No third-party CSS frameworks, fonts, icon sets or images are included. The design, stylesheet and icons
were written for this project; fonts come from the visitor's operating system.

Regenerate the package list at any time with:

    dotnet list package --include-transitive
