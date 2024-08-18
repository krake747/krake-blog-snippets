using System.Data;
using Dapper;
using Krake.Snippets.Dapper;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<IDbConnectionFactory>(sp =>
{
    var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("BookStore");
    return new SqliteConnectionFactory(connectionString!);
});

var app = builder.Build();

app.MapGet("/", () => "Hello BookStore!");

// Basic Commands with Dapper
app.MapPost("/customers", async (IDbConnectionFactory dbConnectionFactory, Customer customer) =>
{
    using var connection = await dbConnectionFactory.CreateConnectionAsync();

    const string sql = // lang=sql
        """
        INSERT INTO Customers (Name, Email)
        VALUES (@Name, @Email)
        RETURNING Id;
        """;

    customer.Id = await connection.ExecuteScalarAsync<int>(sql, customer);

    return customer.Id is -1 ? Results.BadRequest() : Results.Created($"/customers/{customer.Id}", customer);
});

// Basic Querying with Dapper
app.MapGet("/customers/{id:int}", async (IDbConnectionFactory dbConnectionFactory, int id) =>
{
    using var connection = await dbConnectionFactory.CreateConnectionAsync();

    const string sql = // lang=sql
        """
        SELECT Id, Name, Email
        FROM Customers
        WHERE Id = @Id
        """;

    var customer = await connection.QuerySingleOrDefaultAsync<Customer>(sql, new { Id = id });

    return customer is null ? Results.NotFound() : Results.Ok(customer);
});

// Mapping complex types with Dapper
app.MapGet("/books", async (IDbConnectionFactory dbConnectionFactory) =>
{
    using var connection = await dbConnectionFactory.CreateConnectionAsync();

    const string sql = //lang=sql
        """
            SELECT
                b.Isbn, b.Title, b.Price,
                a.Id, a.Name
            FROM Books b
                LEFT JOIN BookAuthors ba ON b.Isbn = ba.BookIsbn
                LEFT JOIN Authors a ON ba.AuthorId = a.Id
        """;

    var books = new Dictionary<string, Book>();
    _ = await connection.QueryAsync<Book, Author, Book>(
        sql,
        (book, author) =>
        {
            if (books.TryGetValue(book.Isbn, out var existingBook) is false)
            {
                existingBook = book;
                books.Add(existingBook.Isbn, existingBook);
            }

            existingBook.Authors.Add(author);
            return existingBook;
        },
        splitOn: "Id"
    );

    return Results.Ok(new { Books = books.Values });
});

// Mapping complex types with Dapper
app.MapGet("/orders", async (IDbConnectionFactory dbConnectionFactory) =>
{
    using var connection = await dbConnectionFactory.CreateConnectionAsync();

    const string sql = //lang=sql
        """
        SELECT
            o.Id, o.OrderDate,
            c.Id, c.Name, c.Email,
            b.Isbn, b.Title
        FROM Orders o
            JOIN Customers c ON o.CustomerId = c.Id
            LEFT JOIN OrderBooks ob ON o.Id = ob.OrderId
            LEFT JOIN Books b ON ob.BookIsbn = b.Isbn
        """;

    var orders = new Dictionary<int, Order>();
    _ = await connection.QueryAsync<Order, Customer, Book, Order>(
        sql,
        (order, customer, book) =>
        {
            if (orders.TryGetValue(order.Id, out var existingOrder) is false)
            {
                existingOrder = order;
                existingOrder.Customer = customer;
                orders.Add(existingOrder.Id, existingOrder);
            }

            existingOrder.Books.Add(book);
            return existingOrder;
        },
        splitOn: "Id,Id,Isbn"
    );

    return Results.Ok(new
    {
        Orders = orders.Values.Select(o => new
        {
            o.Id,
            o.OrderDate,
            o.Customer,
            Books = o.Books.Select(b => new
            {
                b.Isbn,
                b.Title
            })
        })
    });
});

// Handling multiple result sets with Dapper
app.MapGet("/sales-statistics", async (IDbConnectionFactory dbConnectionFactory) =>
{
    using var connection = await dbConnectionFactory.CreateConnectionAsync();

    const string sql = // lang=sql
        """
        SELECT COUNT(*) AS TotalOrders
        FROM Orders;

        SELECT SUM(b.Price * ob.Quantity) AS TotalSales
        FROM Orders o
        JOIN OrderBooks ob ON o.Id = ob.OrderId
        JOIN Books b ON ob.BookIsbn = b.Isbn;

        SELECT b.Title, SUM(ob.Quantity) AS TotalSold
        FROM Books b
        JOIN OrderBooks ob ON b.Isbn = ob.BookIsbn
        GROUP BY b.Title
        ORDER BY TotalSold DESC
        LIMIT 5;
        """;

    await using var result = await connection.QueryMultipleAsync(sql);
    var totalOrders = await result.ReadSingleAsync<int>();
    var totalSales = await result.ReadSingleAsync<decimal>();
    var topSellingBooks = (await result.ReadAsync<TopSellingBook>()).AsList();

    return Results.Ok(new
    {
        TotalOrders = totalOrders,
        TotalSales = totalSales,
        TopSellingBooks = topSellingBooks
    });
});

using (var scope = app.Services.CreateScope())
{
    var dbConnectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
    await new DatabaseInitializer(dbConnectionFactory).InitializeAsync();
}

app.Run();

namespace Krake.Snippets.Dapper
{
    public sealed class Book
    {
        public string Isbn { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public List<Author> Authors { get; set; } = [];
    }

    public sealed class Author
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public sealed class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public sealed class Order
    {
        public int Id { get; set; }
        public DateTime OrderDate { get; set; }
        public Customer Customer { get; set; } = null!;
        public List<Book> Books { get; set; } = [];
    }

    public sealed class TopSellingBook
    {
        public string Title { get; set; } = string.Empty;
        public int TotalSold { get; set; }
    }

    public interface IDbConnectionFactory
    {
        Task<IDbConnection> CreateConnectionAsync(CancellationToken token = default);
    }

    public sealed class SqliteConnectionFactory(string connectionString) : IDbConnectionFactory
    {
        public async Task<IDbConnection> CreateConnectionAsync(CancellationToken token = default)
        {
            var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(token);
            return connection;
        }
    }

    public sealed class DatabaseInitializer(IDbConnectionFactory connectionFactory)
    {
        public async Task InitializeAsync()
        {
            using var connection = await connectionFactory.CreateConnectionAsync();
            await connection.ExecuteAsync( // lang=sql
                """
                -- Drop the dependent tables first to avoid foreign key constraint issues
                DROP TABLE IF EXISTS OrderBooks;
                DROP TABLE IF EXISTS Orders;
                DROP TABLE IF EXISTS Inventory;
                DROP TABLE IF EXISTS BookAuthors;

                -- Drop the independent tables
                DROP TABLE IF EXISTS Customers;
                DROP TABLE IF EXISTS Books;
                DROP TABLE IF EXISTS Authors;

                CREATE TABLE IF NOT EXISTS Books (
                    Isbn TEXT PRIMARY KEY,
                    Title TEXT NOT NULL,
                    Price REAL NOT NULL
                );

                CREATE TABLE IF NOT EXISTS Authors (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS BookAuthors (
                    BookIsbn TEXT NOT NULL,
                    AuthorId INTEGER NOT NULL,
                    FOREIGN KEY (BookIsbn) REFERENCES Books(Isbn) ON DELETE CASCADE,
                    FOREIGN KEY (AuthorId) REFERENCES Authors(Id) ON DELETE CASCADE,
                    PRIMARY KEY (BookIsbn, AuthorId)
                );

                CREATE TABLE IF NOT EXISTS Inventory (
                    BookIsbn TEXT PRIMARY KEY,
                    Quantity INTEGER NOT NULL,
                    FOREIGN KEY (BookIsbn) REFERENCES Books(Isbn) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS Customers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Email TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS Orders (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CustomerId INTEGER NOT NULL,
                    OrderDate TEXT NOT NULL,
                    FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS OrderBooks (
                    OrderId INTEGER NOT NULL,
                    BookIsbn TEXT NOT NULL,
                    Quantity INTEGER NOT NULL,
                    FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE CASCADE,
                    FOREIGN KEY (BookIsbn) REFERENCES Books(Isbn) ON DELETE CASCADE,
                    PRIMARY KEY (OrderId, BookIsbn)
                );

                INSERT INTO Books (Isbn, Title, Price) VALUES ('978-0985580155', 'C# Player''s Guide', 34.95);
                INSERT INTO Books (Isbn, Title, Price) VALUES ('978-0374275631', 'Thinking, Fast and Slow', 22.00);
                INSERT INTO Books (Isbn, Title, Price) VALUES ('978-0201633610', 'Design Patterns: Elements of Reusable Object-Oriented Software', 59.99);

                INSERT INTO Authors (Name) VALUES ('R.B. Whitaker');
                INSERT INTO Authors (Name) VALUES ('Daniel Kahneman');
                INSERT INTO Authors (Name) VALUES ('Erich Gamma');
                INSERT INTO Authors (Name) VALUES ('Richard Helm');
                INSERT INTO Authors (Name) VALUES ('Ralph Johnson');
                INSERT INTO Authors (Name) VALUES ('John Vlissides');

                INSERT INTO BookAuthors (BookIsbn, AuthorId) VALUES ('978-0985580155', (SELECT Id FROM Authors WHERE Name = 'R.B. Whitaker'));
                INSERT INTO BookAuthors (BookIsbn, AuthorId) VALUES ('978-0374275631', (SELECT Id FROM Authors WHERE Name = 'Daniel Kahneman'));
                INSERT INTO BookAuthors (BookIsbn, AuthorId) VALUES ('978-0201633610', (SELECT Id FROM Authors WHERE Name = 'Erich Gamma'));
                INSERT INTO BookAuthors (BookIsbn, AuthorId) VALUES ('978-0201633610', (SELECT Id FROM Authors WHERE Name = 'Richard Helm'));
                INSERT INTO BookAuthors (BookIsbn, AuthorId) VALUES ('978-0201633610', (SELECT Id FROM Authors WHERE Name = 'Ralph Johnson'));
                INSERT INTO BookAuthors (BookIsbn, AuthorId) VALUES ('978-0201633610', (SELECT Id FROM Authors WHERE Name = 'John Vlissides'));

                INSERT INTO Inventory (BookIsbn, Quantity) VALUES ('978-0985580155', 5);
                INSERT INTO Inventory (BookIsbn, Quantity) VALUES ('978-0374275631', 10);
                INSERT INTO Inventory (BookIsbn, Quantity) VALUES ('978-0201633610', 3);

                INSERT INTO Customers (Name, Email) VALUES ('John Doe', 'john.doe@example.com');
                INSERT INTO Customers (Name, Email) VALUES ('Jane Smith', 'jane.smith@example.com');

                INSERT INTO Orders (CustomerId, OrderDate) VALUES (1, '2024-08-16');
                INSERT INTO Orders (CustomerId, OrderDate) VALUES (2, '2024-08-16');

                INSERT INTO OrderBooks (OrderId, BookIsbn, Quantity) VALUES (1, '978-0374275631', 2);
                INSERT INTO OrderBooks (OrderId, BookIsbn, Quantity) VALUES (2, '978-0985580155', 1);
                INSERT INTO OrderBooks (OrderId, BookIsbn, Quantity) VALUES (2, '978-0201633610', 1);
                """
            );
        }
    }
}