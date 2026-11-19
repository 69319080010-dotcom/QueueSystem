using Microsoft.Data.Sqlite;
using QueueSystem.Models;

namespace QueueSystem.Services;

public class QueueService
{
    private const int Waiting = (int)QueueStatus.Waiting;
    private const int Called = (int)QueueStatus.Called;
    private const string Columns = """
        sequence, id, service_code, ticket_number, customer_name,
        created_ticks, called_ticks, status, remark
        """;

    private readonly string _connectionString;

    public static readonly IReadOnlyList<ServiceType> ServiceTypes = new[]
    {
        new ServiceType("A", "รับเอกสาร", "📥"),
        new ServiceType("B", "ขอหนังสือรับรอง", "📜"),
        new ServiceType("C", "ยื่นคำร้องทั่วไป", "📝"),
    };

    public event Action? OnChange;

    public QueueService(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var setup = connection.CreateCommand();
        setup.Transaction = transaction;
        setup.CommandText = """
            CREATE TABLE IF NOT EXISTS queue_tickets (
                sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                id TEXT NOT NULL UNIQUE,
                service_code TEXT NOT NULL CHECK (service_code IN ('A', 'B', 'C')),
                ticket_number INTEGER NOT NULL CHECK (ticket_number > 0),
                customer_name TEXT NOT NULL,
                created_ticks INTEGER NOT NULL,
                called_ticks INTEGER,
                status INTEGER NOT NULL DEFAULT 0 CHECK (status IN (0, 1, 2, 3)),
                remark TEXT,
                UNIQUE (service_code, ticket_number)
            );
            CREATE INDEX IF NOT EXISTS ix_tickets_created
                ON queue_tickets(created_ticks, sequence);
            CREATE INDEX IF NOT EXISTS ix_tickets_service_status_created
                ON queue_tickets(service_code, status, created_ticks, sequence);
            """;
        setup.ExecuteNonQuery();

        setup.CommandText = "SELECT EXISTS (SELECT 1 FROM queue_tickets);";
        if (Convert.ToInt64(setup.ExecuteScalar()) == 0)
        {
            var names = new[] { "สมชาย ใจดี", "สมหญิง รักเรียน", "วิชัย มั่นคง", "มาลี สุขสันต์", "ประเสริฐ ทองดี" };
            for (var i = 0; i < names.Length; i++)
                Insert(connection, transaction, ServiceTypes[i % ServiceTypes.Count].Code, names[i], null);
        }
        transaction.Commit();
    }

    public QueueTicket Create(string serviceCode, string customerName, string? remark = null)
    {
        var name = string.IsNullOrWhiteSpace(customerName) ? "ผู้รับบริการทั่วไป" : customerName.Trim();
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var ticket = Insert(connection, transaction, serviceCode, name, remark);
        transaction.Commit();
        OnChange?.Invoke();
        return ticket;
    }

    private static QueueTicket Insert(SqliteConnection connection, SqliteTransaction transaction,
        string serviceCode, string name, string? remark)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO queue_tickets (id, service_code, ticket_number, customer_name, created_ticks, remark)
            VALUES ($id, $service, (
                SELECT COALESCE(MAX(ticket_number), 0) + 1 FROM queue_tickets WHERE service_code = $service
            ), $name, $created, $remark)
            RETURNING {Columns};
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$service", serviceCode);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$created", DateTime.Now.Ticks);
        command.Parameters.AddWithValue("$remark", (object?)remark ?? DBNull.Value);
        return ReadTickets(command).Single();
    }

    public QueueTicket? CallNext(string serviceCode, DateOnly? date = null)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE queue_tickets
            SET status = $called, called_ticks = $now
            WHERE sequence = (
                SELECT sequence FROM queue_tickets
                WHERE service_code = $service AND status = $waiting
                    AND created_ticks >= $start AND created_ticks < $end
                ORDER BY created_ticks, ticket_number
                LIMIT 1
            )
            RETURNING {Columns};
            """;
        AddDateRange(command, date ?? Today);
        command.Parameters.AddWithValue("$service", serviceCode);
        command.Parameters.AddWithValue("$waiting", Waiting);
        command.Parameters.AddWithValue("$called", Called);
        command.Parameters.AddWithValue("$now", DateTime.Now.Ticks);
        var ticket = ReadTickets(command).SingleOrDefault();
        transaction.Commit();
        if (ticket is not null) OnChange?.Invoke();
        return ticket;
    }

    public bool Cancel(Guid id, string? remark = null) =>
        Update(id, QueueStatus.Waiting, QueueStatus.Cancelled, remark);

    public bool Complete(Guid id) =>
        Update(id, QueueStatus.Called, QueueStatus.Completed, null);

    private bool Update(Guid id, QueueStatus from, QueueStatus to, string? remark)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE queue_tickets
            SET status = $to, remark = COALESCE($remark, remark)
            WHERE id = $id AND status = $from;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$from", (int)from);
        command.Parameters.AddWithValue("$to", (int)to);
        command.Parameters.AddWithValue("$remark", string.IsNullOrWhiteSpace(remark) ? DBNull.Value : remark);
        if (command.ExecuteNonQuery() != 1) return false;
        transaction.Commit();
        OnChange?.Invoke();
        return true;
    }

    public List<QueueTicket> GetToday(string? serviceCode = null) => GetByDate(Today, serviceCode);

    public List<QueueTicket> GetByDate(DateOnly date, string? serviceCode = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {Columns} FROM queue_tickets
            WHERE created_ticks >= $start AND created_ticks < $end
                AND ($service IS NULL OR service_code = $service)
            ORDER BY created_ticks, sequence;
            """;
        AddDateRange(command, date);
        command.Parameters.AddWithValue("$service", (object?)serviceCode ?? DBNull.Value);
        return ReadTickets(command);
    }

    public QueueTicket? GetCurrent(string serviceCode, DateOnly? date = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {Columns} FROM queue_tickets
            WHERE service_code = $service AND status = $called
                AND created_ticks >= $start AND created_ticks < $end
            ORDER BY called_ticks DESC, sequence DESC LIMIT 1;
            """;
        AddDateRange(command, date ?? Today);
        command.Parameters.AddWithValue("$service", serviceCode);
        command.Parameters.AddWithValue("$called", Called);
        return ReadTickets(command).SingleOrDefault();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    private static void AddDateRange(SqliteCommand command, DateOnly date)
    {
        var start = date.ToDateTime(TimeOnly.MinValue).Ticks;
        command.Parameters.AddWithValue("$start", start);
        command.Parameters.AddWithValue("$end", start + TimeSpan.TicksPerDay);
    }

    private static List<QueueTicket> ReadTickets(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var tickets = new List<QueueTicket>();
        while (reader.Read())
        {
            var service = ServiceTypes.First(type => type.Code == reader.GetString(2));
            tickets.Add(new QueueTicket
            {
                Sequence = reader.GetInt32(0),
                Id = Guid.Parse(reader.GetString(1)),
                Service = service,
                Number = $"{service.Code}{reader.GetInt32(3):000}",
                CustomerName = reader.GetString(4),
                CreatedAt = new DateTime(reader.GetInt64(5), DateTimeKind.Local),
                CalledAt = reader.IsDBNull(6) ? null : new DateTime(reader.GetInt64(6), DateTimeKind.Local),
                Status = (QueueStatus)reader.GetInt32(7),
                Remark = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }
        return tickets;
    }
}
