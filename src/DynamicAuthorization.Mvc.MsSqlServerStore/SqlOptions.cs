namespace DynamicAuthorization.Mvc.MsSqlServerStore
{
    public class SqlOptions
    {
        public string ConnectionString { get; set; }

        internal bool IsTableCreated { get; set; }
    }
}