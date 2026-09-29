using System.Collections.Generic;

namespace Jogging.Profile
{
    /// <summary>
    /// Persistence boundary for the runner profiles. The MVP uses <see cref="LocalProfileStore"/>
    /// (one JSON file per runner). A cloud store could implement this same interface later.
    /// </summary>
    public interface IProfileStore
    {
        List<ProfileData> LoadAll();
        /// <summary>Save (assigns id/created if missing); false if it could not be written.</summary>
        bool Save(ProfileData data);
        void Delete(string id);
    }
}
