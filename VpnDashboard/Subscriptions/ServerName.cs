namespace VpnDashboard.Subscriptions;

/// <summary>Отображаемые имена серверов в агрегированной подписке.</summary>
public static class ServerName
{
    /// <summary>
    /// Приписывает тег источника к имени сервера.
    /// <para>
    /// Флаг страны в начале имени клиенты распознают и показывают отдельным значком,
    /// поэтому тег вставляется ПОСЛЕ флага — иначе значок теряется и остаётся просто текстом.
    /// </para>
    /// </summary>
    public static string WithPrefix(string name, string prefix)
    {
        var flagLength = LeadingFlagLength(name);
        if (flagLength == 0)
            return $"[{prefix}] {name}";

        var flag = name[..flagLength];
        var rest = name[flagLength..].TrimStart();

        return rest.Length == 0 ? $"{flag} [{prefix}]" : $"{flag} [{prefix}] {rest}";
    }

    /// <summary>Длина флага в начале строки (в символах UTF-16); 0 — флага нет.</summary>
    private static int LeadingFlagLength(string name)
    {
        // Обычный флаг страны — пара «региональных индикаторов», например 🇫 + 🇮.
        if (RuneAt(name, 0) is >= 0x1F1E6 and <= 0x1F1FF &&
            RuneAt(name, 2) is >= 0x1F1E6 and <= 0x1F1FF)
            return 4;

        // Флаг региона — 🏴 плюс tag-последовательность с терминатором, например 🏴󠁧󠁢󠁳󠁣󠁴󠁿.
        if (RuneAt(name, 0) != 0x1F3F4)
            return 0;

        var i = 2;
        while (RuneAt(name, i) is >= 0xE0020 and <= 0xE007E)
            i += 2;

        return RuneAt(name, i) == 0xE007F ? i + 2 : 0;
    }

    /// <summary>Кодовая точка в позиции index или -1, если там нет суррогатной пары.</summary>
    private static int RuneAt(string value, int index) =>
        index + 1 < value.Length && char.IsSurrogatePair(value, index)
            ? char.ConvertToUtf32(value, index)
            : -1;
}
