using System;

public sealed class P0ActivationGuard
{
    private bool active;
    private string companyId = string.Empty;

    public int Generation { get; private set; }

    public int Activate(string selectedCompanyId)
    {
        active = true;
        companyId = selectedCompanyId ?? string.Empty;
        Generation++;
        return Generation;
    }

    public void Deactivate()
    {
        active = false;
        companyId = string.Empty;
        Generation++;
    }

    public bool IsCurrent(int generation, string selectedCompanyId)
    {
        return active && generation == Generation &&
               string.Equals(companyId, selectedCompanyId ?? string.Empty, StringComparison.Ordinal);
    }
}
