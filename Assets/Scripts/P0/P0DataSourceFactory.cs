public static class P0DataSourceFactory
{
    public static IP0DataSource Create(P0DataMode mode)
    {
        if (mode == P0DataMode.Mock) return new MockP0DataSource();
        return new NetworkP0DataSource();
    }
}
