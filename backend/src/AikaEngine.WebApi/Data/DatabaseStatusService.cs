using Npgsql;

namespace AikaEngine.WebApi.Data;

public interface IDatabaseStatusService
{
    bool IsAvailable { get; }
    string ConnectionString { get; }
    Task<bool> CheckAvailabilityAsync();
}

public class DatabaseStatusService : IDatabaseStatusService
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseStatusService> _logger;
    private volatile bool _isAvailable = false;
    private DateTime _lastCheck = DateTime.MinValue;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30);

    public DatabaseStatusService(IConfiguration config, ILogger<DatabaseStatusService> logger)
    {
        _logger = logger;
        var rawConn = config.GetConnectionString("DefaultConnection") ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(rawConn))
        {
            try
            {
                var builder = new NpgsqlConnectionStringBuilder(rawConn)
                {
                    Timeout = 1,
                    CommandTimeout = 1
                };
                _connectionString = builder.ConnectionString;
            }
            catch
            {
                _connectionString = rawConn;
            }
        }
        else
        {
            _connectionString = string.Empty;
        }

        // Run non-blocking check on initialization
        Task.Run(CheckAvailabilityAsync);
    }

    public bool IsAvailable => _isAvailable;
    public string ConnectionString => _connectionString;

    public async Task<bool> CheckAvailabilityAsync()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            _isAvailable = false;
            return false;
        }

        if (DateTime.UtcNow - _lastCheck < _checkInterval && _lastCheck != DateTime.MinValue)
        {
            return _isAvailable;
        }

        _lastCheck = DateTime.UtcNow;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cts.Token);
            _isAvailable = true;
            _logger.LogInformation("PostgreSQL is connected and available.");
            return true;
        }
        catch (Exception ex)
        {
            if (_isAvailable)
            {
                _logger.LogWarning("PostgreSQL is currently unreachable ({Message}). Using lightning-fast in-memory fallback.", ex.Message);
            }
            _isAvailable = false;
            return false;
        }
    }
}
