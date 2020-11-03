namespace DynamicAuthorization.Mvc.JsonStore
{
    public class JsonOptions
    {
        internal const string DefaultRoleStoreName = "RoleAccess.json";

        public string FilePath { get; set; } = DefaultRoleStoreName;
    }
}