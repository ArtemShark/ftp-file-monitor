using Microsoft.Data.Sqlite;

class KnownFile
{
    public long Size;
    public bool Confirmed;
    public bool Baseline;
}

static class Database
{
    static string connectionString => "Data Source=" + Settings.DbFile;

    public static void CreateTables()
    {
        using var db = new SqliteConnection(connectionString);
        db.Open();

        var cmd = db.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS known_files (
                path TEXT PRIMARY KEY,
                size INTEGER NOT NULL,
                modified_server TEXT,
                first_seen_utc TEXT NOT NULL,
                confirmed INTEGER NOT NULL DEFAULT 0,
                baseline INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS detections (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                path TEXT NOT NULL,
                rule TEXT,
                size INTEGER,
                modified_server TEXT,
                detected_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS scan_runs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                started_utc TEXT NOT NULL,
                finished_utc TEXT,
                matched INTEGER,
                new_detected INTEGER,
                success INTEGER NOT NULL,
                error TEXT
            );

            CREATE INDEX IF NOT EXISTS ix_detections_time ON detections(detected_utc);
            CREATE INDEX IF NOT EXISTS ix_detections_rule ON detections(rule);
        ";
        cmd.ExecuteNonQuery();
    }

    public static bool HasSuccessfulScan()
    {
        using var db = new SqliteConnection(connectionString);
        db.Open();

        var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM scan_runs WHERE success = 1";

        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    public static List<FileItem> SaveResults(List<FileItem> foundFiles, DateTime started, bool firstRun)
    {
        using var db = new SqliteConnection(connectionString);
        db.Open();

        Dictionary<string, KnownFile> knownFiles = LoadKnownFiles(db);

        string now = FormatTime(DateTime.UtcNow);
        List<FileItem> newFiles = new List<FileItem>();

        using var tx = db.BeginTransaction();

        foreach (FileItem file in foundFiles)
        {
            bool isNew = !knownFiles.ContainsKey(file.Path);
            bool canCheckSize = Settings.WaitForStableSize && file.Size != Settings.UnknownSize;

            bool confirmed;
            bool baseline;
            bool report;

            if (firstRun)
            {
                baseline = true;
                confirmed = !canCheckSize;
                report = false;
            }
            else if (isNew)
            {
                baseline = false;
                confirmed = !canCheckSize;
                report = confirmed;
            }
            else
            {
                KnownFile known = knownFiles[file.Path];

                if (known.Confirmed)
                    continue;

                if (file.Size != known.Size)
                {
                    confirmed = false;
                    baseline = false;
                    report = false;
                }
                else
                {
                    confirmed = true;
                    baseline = known.Baseline;
                    report = !known.Baseline;
                }
            }

            if (isNew)
                InsertKnownFile(db, tx, file, now, confirmed, baseline);
            else
                UpdateKnownFile(db, tx, file, confirmed, baseline);

            if (report)
            {
                InsertDetection(db, tx, file, now);
                file.DetectedUtc = now;
                newFiles.Add(file);
            }
        }

        SaveScanRun(db, tx, started, now, foundFiles.Count, newFiles.Count);

        tx.Commit();

        return newFiles;
    }

    public static void SaveError(DateTime started, string error)
    {
        try
        {
            using var db = new SqliteConnection(connectionString);
            db.Open();

            var cmd = db.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO scan_runs (started_utc, finished_utc, success, error)
                VALUES ($started, $finished, 0, $error)";

            cmd.Parameters.AddWithValue("$started", FormatTime(started));
            cmd.Parameters.AddWithValue("$finished", FormatTime(DateTime.UtcNow));
            cmd.Parameters.AddWithValue("$error", error);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Could not write scan_runs: " + ex.Message);
        }
    }

    static Dictionary<string, KnownFile> LoadKnownFiles(SqliteConnection db)
    {
        Dictionary<string, KnownFile> result = new Dictionary<string, KnownFile>();

        var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT path, size, confirmed, baseline FROM known_files";

        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                KnownFile file = new KnownFile();
                file.Size = reader.IsDBNull(1) ? Settings.UnknownSize : reader.GetInt64(1);
                file.Confirmed = reader.GetInt32(2) == 1;
                file.Baseline = reader.GetInt32(3) == 1;

                result[reader.GetString(0)] = file;
            }
        }

        return result;
    }

    static void InsertKnownFile(SqliteConnection db, SqliteTransaction tx, FileItem file,
                                string now, bool confirmed, bool baseline)
    {
        var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO known_files (path, size, modified_server, first_seen_utc, confirmed, baseline)
            VALUES ($path, $size, $modified, $now, $confirmed, $baseline)";

        cmd.Parameters.AddWithValue("$path", file.Path);
        cmd.Parameters.AddWithValue("$size", file.Size);
        cmd.Parameters.AddWithValue("$modified", FormatTime(file.Modified));
        cmd.Parameters.AddWithValue("$now", now);
        cmd.Parameters.AddWithValue("$confirmed", confirmed ? 1 : 0);
        cmd.Parameters.AddWithValue("$baseline", baseline ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    static void UpdateKnownFile(SqliteConnection db, SqliteTransaction tx, FileItem file,
                                bool confirmed, bool baseline)
    {
        var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            UPDATE known_files
            SET size = $size, confirmed = $confirmed, baseline = $baseline
            WHERE path = $path";

        cmd.Parameters.AddWithValue("$path", file.Path);
        cmd.Parameters.AddWithValue("$size", file.Size);
        cmd.Parameters.AddWithValue("$confirmed", confirmed ? 1 : 0);
        cmd.Parameters.AddWithValue("$baseline", baseline ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    static void InsertDetection(SqliteConnection db, SqliteTransaction tx, FileItem file, string now)
    {
        var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO detections (path, rule, size, modified_server, detected_utc)
            VALUES ($path, $rule, $size, $modified, $now)";

        cmd.Parameters.AddWithValue("$path", file.Path);
        cmd.Parameters.AddWithValue("$rule", file.Rule);
        cmd.Parameters.AddWithValue("$size", file.Size);
        cmd.Parameters.AddWithValue("$modified", FormatTime(file.Modified));
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
    }

    static void SaveScanRun(SqliteConnection db, SqliteTransaction tx, DateTime started,
                            string finished, int matched, int newCount)
    {
        var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO scan_runs (started_utc, finished_utc, matched, new_detected, success)
            VALUES ($started, $finished, $matched, $new, 1)";

        cmd.Parameters.AddWithValue("$started", FormatTime(started));
        cmd.Parameters.AddWithValue("$finished", finished);
        cmd.Parameters.AddWithValue("$matched", matched);
        cmd.Parameters.AddWithValue("$new", newCount);
        cmd.ExecuteNonQuery();
    }

    static string FormatTime(DateTime value)
    {
        return value.ToString("yyyy-MM-dd HH:mm:ss");
    }
}