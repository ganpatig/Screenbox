using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Screenbox.Core.Models;

namespace Screenbox.Core.Services;

public sealed partial class DatabaseService
{
    /// <inheritdoc/>
    public async Task ReplaceTrackPreferencesAsync(IReadOnlyList<MediaTrackPreference> snapshot)
    {
        await EnsureInitializedAsync();
        using var connection = CreateConnection();
        using var transaction = connection.BeginTransaction();

        ExecuteNonQuery(connection, "DELETE FROM track_preferences;");

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO track_preferences
                    (location, audio_index, audio_language, audio_label,
                     subtitle_index, subtitle_language, subtitle_label, updated_ticks)
                VALUES (@loc, @ai, @al, @alabel, @si, @sl, @slabel, @ticks);
                """;
            var pLoc = cmd.Parameters.Add("@loc", SqliteType.Text);
            var pAudioIndex = cmd.Parameters.Add("@ai", SqliteType.Integer);
            var pAudioLanguage = cmd.Parameters.Add("@al", SqliteType.Text);
            var pAudioLabel = cmd.Parameters.Add("@alabel", SqliteType.Text);
            var pSubtitleIndex = cmd.Parameters.Add("@si", SqliteType.Integer);
            var pSubtitleLanguage = cmd.Parameters.Add("@sl", SqliteType.Text);
            var pSubtitleLabel = cmd.Parameters.Add("@slabel", SqliteType.Text);
            var pTicks = cmd.Parameters.Add("@ticks", SqliteType.Integer);

            foreach (MediaTrackPreference item in snapshot)
            {
                pLoc.Value = item.Location;
                pAudioIndex.Value = (object?)item.Audio?.Index ?? DBNull.Value;
                pAudioLanguage.Value = (object?)item.Audio?.LanguageTag ?? DBNull.Value;
                pAudioLabel.Value = (object?)item.Audio?.Label ?? DBNull.Value;
                pSubtitleIndex.Value = (object?)item.Subtitle?.Index ?? DBNull.Value;
                pSubtitleLanguage.Value = (object?)item.Subtitle?.LanguageTag ?? DBNull.Value;
                pSubtitleLabel.Value = (object?)item.Subtitle?.Label ?? DBNull.Value;
                pTicks.Value = item.Updated.UtcTicks;
                cmd.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    /// <inheritdoc/>
    public async Task<List<MediaTrackPreference>> LoadTrackPreferencesAsync()
    {
        await EnsureInitializedAsync();
        using var connection = CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT location, audio_index, audio_language, audio_label,
                   subtitle_index, subtitle_language, subtitle_label, updated_ticks
            FROM track_preferences
            ORDER BY updated_ticks DESC;
            """;

        var result = new List<MediaTrackPreference>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string location = reader.GetString(0);
            MediaTrackChoice? audio = ReadChoice(reader, 1);
            MediaTrackChoice? subtitle = ReadChoice(reader, 4);
            DateTimeOffset updated = new(reader.GetInt64(7), TimeSpan.Zero);
            result.Add(new MediaTrackPreference(location, audio, subtitle, updated));
        }

        return result;
    }

    /// <summary>
    /// Reads a track choice from three consecutive columns, which are null when nothing was ever chosen.
    /// </summary>
    private static MediaTrackChoice? ReadChoice(SqliteDataReader reader, int indexColumn)
    {
        if (reader.IsDBNull(indexColumn))
        {
            return null;
        }

        int index = reader.GetInt32(indexColumn);
        string language = reader.IsDBNull(indexColumn + 1) ? string.Empty : reader.GetString(indexColumn + 1);
        string label = reader.IsDBNull(indexColumn + 2) ? string.Empty : reader.GetString(indexColumn + 2);
        return new MediaTrackChoice(index, language, label);
    }
}
