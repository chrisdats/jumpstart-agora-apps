﻿using Npgsql;
using NpgsqlTypes;

namespace Contoso.Backend.Data.Services
{
    public class PostgreSqlService
    {
        private readonly string _connectionString;

        public PostgreSqlService(string connectionString)
        {
            _connectionString = connectionString;
            CreateCheckoutTablesIfNotExists();
            SeedCheckoutTypeTableIfEmpty();
        }

        private void SeedCheckoutTypeTableIfEmpty()
        {
            var checkoutTypePopulated = TableHasValue("contoso.checkout_type").Result;
            if (!checkoutTypePopulated)
            {
                using var con = new NpgsqlConnection(_connectionString);
                con.Open();
                var sql = @"
                    INSERT INTO contoso.checkout_type(Id, Name)
                    VALUES 
                        (@StandardId, @StandardName),
                        (@ExpressId, @ExpressName),
                        (@SelfServiceId, @SelfServiceName)
                ";
                using var cmd = new NpgsqlCommand(sql, con)
                {
                    Parameters =
                    {
                        new NpgsqlParameter("StandardId",(int)CheckoutType.Standard),
                        new NpgsqlParameter("StandardName",CheckoutType.Standard.ToString()),
                        new NpgsqlParameter("ExpressId",(int)CheckoutType.Express),
                        new NpgsqlParameter("ExpressName",CheckoutType.Express.ToString()),
                        new NpgsqlParameter("SelfServiceId",(int)CheckoutType.SelfService),
                        new NpgsqlParameter("SelfServiceName",CheckoutType.SelfService.ToString())
                    }
                };
                cmd.ExecuteNonQuery();
                con.Close();
            }
        }

        private void CreateCheckoutTablesIfNotExists()
        {
            var con = new NpgsqlConnection(_connectionString);
            con.Open();
            var sql = @"
                CREATE TABLE IF NOT EXISTS contoso.checkout_history (
                    timestamp TIMESTAMPTZ,
                    checkout_id INT,
                    checkout_type INT,
                    queue_length INT,
                    average_wait_time_seconds INT
                );
                CREATE TABLE IF NOT EXISTS contoso.checkout_type (
                    id INT,
                    name VARCHAR(20)
                );
            ";
            var cmd = new NpgsqlCommand(sql, con);
            cmd.ExecuteNonQuery();
            con.Close();
        }

        public async Task<bool> TableHasValue(string tableName)
        {
            using var con = new NpgsqlConnection(_connectionString);
            await con.OpenAsync();
            var sql = $"select true from {tableName} limit 1;";
            using var cmd = new NpgsqlCommand(sql, con);
            var res = cmd.ExecuteScalar();
            await con.CloseAsync();

            return (bool)(res ?? false);
        }

        public async Task BulkUpsertCheckoutHistory(List<CheckoutHistory> history)
        {
            await using var con = new NpgsqlConnection(_connectionString);

            await con.OpenAsync();

            using var importer = con.BeginBinaryImport(
                       "COPY contoso.checkout_history (timestamp, checkout_id, checkout_type, queue_length, average_wait_time_seconds) FROM STDIN (FORMAT binary)");

            foreach (var element in history)
            {
                await importer.StartRowAsync();
                await importer.WriteAsync(element.Timestamp, NpgsqlDbType.TimestampTz);
                await importer.WriteAsync(element.CheckoutId, NpgsqlDbType.Integer);
                await importer.WriteAsync((int)element.CheckoutType, NpgsqlDbType.Integer);
                await importer.WriteAsync(element.QueueLength, NpgsqlDbType.Integer);
                await importer.WriteAsync(element.AverageWaitTimeSeconds, NpgsqlDbType.Integer);
            }

            await importer.CompleteAsync();
            return;
        }

        public async Task<DateTimeOffset> GetMaxCheckoutHistoryTimestamp()
        {
            using var con = new NpgsqlConnection(_connectionString);
            await con.OpenAsync();
            var sql = $"select max(timestamp) from contoso.checkout_history limit 1;";
            using var cmd = new NpgsqlCommand(sql, con);
            var res = cmd.ExecuteScalar();
            await con.CloseAsync();

            return (DateTime)(res ?? DateTime.MinValue);
        }

        public async Task<List<CheckoutHistory>> GetCheckoutHistory(DateTimeOffset? startDateTime = null, DateTimeOffset? endDateTime = null)
        {
            if (startDateTime == null) { startDateTime = DateTimeOffset.MinValue; }
            if (endDateTime == null) { endDateTime = DateTimeOffset.MaxValue; }

            using var con = new NpgsqlConnection(_connectionString);
            await con.OpenAsync();
            var sql = $@"select 
                            timestamp, 
                            checkout_id, 
                            checkout_type, 
                            queue_length, 
                            average_wait_time_seconds 
                        from contoso.checkout_history WHERE timestamp > @minTime AND timestamp <= @maxTime;";
            await using var cmd = new NpgsqlCommand(sql, con)
            {
                Parameters =
                {
                    new NpgsqlParameter("minTime",startDateTime),
                    new NpgsqlParameter("maxTime",endDateTime)
                }
            };

            List<CheckoutHistory> ret = new();

            NpgsqlDataReader res = cmd.ExecuteReader();

            while (res.Read())
            {
                CheckoutHistory item = new()
                {
                    Timestamp = res.GetDateTime(0),
                    CheckoutId = res.GetInt32(1),
                    CheckoutType = (CheckoutType)res.GetInt32(2),
                    QueueLength = res.GetInt32(3),
                    AverageWaitTimeSeconds = res.GetInt32(4)
                };
                ret.Add(item);
            }

            await con.CloseAsync();

            return ret;
        }
    }
}
