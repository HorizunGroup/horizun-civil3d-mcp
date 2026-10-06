namespace Horizun.Civil3D.Core;

/// <summary>Counts inspected objects without converting unreadable states into false or zero.</summary>
public sealed class AuditCounts
{
    public int? Enumerated { get; private set; }
    public int Inspected { get; private set; }
    public int Unreadable { get; private set; }
    public int References { get; private set; }
    public int ReferenceUnknown { get; private set; }
    public int StaleReferences { get; private set; }
    public int StaleUnknown { get; private set; }
    public int InvalidReferences { get; private set; }
    public int ValidityUnknown { get; private set; }
    public int OutOfDate { get; private set; }
    public int Fresh { get; private set; }
    public int CurrencyUnknown { get; private set; }

    public void SetEnumerated(int count) => Enumerated = count;
    public void MarkUnreadable() => Unreadable++;

    public void Record(bool? reference, bool? stale, bool? valid, bool? outOfDate, bool checkCurrency)
    {
        Inspected++;
        if (reference == true)
        {
            References++;
            if (stale == true) StaleReferences++;
            else if (stale == null) StaleUnknown++;
            if (valid == false) InvalidReferences++;
            else if (valid == null) ValidityUnknown++;
        }
        else if (reference == null) ReferenceUnknown++;

        if (!checkCurrency) return;
        if (outOfDate == true) OutOfDate++;
        else if (outOfDate == false) Fresh++;
        else CurrencyUnknown++;
    }
}
