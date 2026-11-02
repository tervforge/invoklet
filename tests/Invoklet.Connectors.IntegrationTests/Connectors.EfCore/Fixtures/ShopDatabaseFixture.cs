using Microsoft.Data.Sqlite;

namespace Invoklet.Connectors.IntegrationTests.Connectors.EfCore.Fixtures;

// A small shop in a SQLite file: tables with keys, a foreign key, a view, a column with a default and a composite key.
internal sealed class ShopDatabaseFixture : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("Invoklet-efcore").FullName;

    public ShopDatabaseFixture()
    {
        using var connection = new SqliteConnection($"Data Source={Path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE customers (id INTEGER PRIMARY KEY, name TEXT NOT NULL, city TEXT, created DATETIME DEFAULT CURRENT_TIMESTAMP);
            CREATE TABLE orders (id INTEGER PRIMARY KEY, customer_id INTEGER NOT NULL REFERENCES customers(id), total DECIMAL(10, 2));
            CREATE TABLE order_items (order_id INTEGER NOT NULL REFERENCES orders(id), line INTEGER NOT NULL, product TEXT NOT NULL, PRIMARY KEY (order_id, line));
            CREATE VIEW big_orders AS SELECT id, customer_id, total FROM orders WHERE total > 100;
            INSERT INTO customers (name, city) VALUES ('Ann', 'Oslo'), ('Bob', 'Paris');
            INSERT INTO orders (customer_id, total) VALUES (1, 150.5), (1, 20), (2, 99);
            INSERT INTO order_items VALUES (1, 1, 'Pen'), (1, 2, 'Ink');
            """;
        command.ExecuteNonQuery();
    }

    public string Folder => _folder;

    public string Path => System.IO.Path.Combine(_folder, "shop.db");

    public string ConnectionString => $"Data Source={Path};Pooling=False";

    public void Dispose() => Directory.Delete(_folder, true);
}
