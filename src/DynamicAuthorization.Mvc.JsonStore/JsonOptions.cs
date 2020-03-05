namespace DynamicAuthorization.Mvc.JsonStore
{
    public class JsonOptions
    {
        public string FilePath { get; set; } = "RoleAccess.json";

        public bool UseMemoryCache { get; set; } = true;
    }
}