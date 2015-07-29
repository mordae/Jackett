﻿using CsQuery;
using Jackett.Models;
using Jackett.Services;
using Jackett.Utils;
using Jackett.Utils.Clients;
using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Jackett.Indexers
{
    class SceneAccess : BaseIndexer, IIndexer
    {
        private string LoginUrl { get { return SiteLink + "login"; } }
        private string SearchUrl { get { return SiteLink + "{0}?method=1&c{1}=1&search={2}"; } }

        public SceneAccess(IIndexerManagerService i, IWebClient c, Logger l)
            : base(name: "SceneAccess",
                description: "Your gateway to the scene",
                link: "https://sceneaccess.eu/",
                caps: TorznabCapsUtil.CreateDefaultTorznabTVCaps(),
                manager: i,
                client: c,
                logger: l)
        {
        }

        public Task<ConfigurationData> GetConfigurationForSetup()
        {
            return Task.FromResult<ConfigurationData>(new ConfigurationDataBasicLogin());
        }

        public async Task ApplyConfiguration(JToken configJson)
        {
            var config = new ConfigurationDataBasicLogin();
            config.LoadValuesFromJson(configJson);

            var pairs = new Dictionary<string, string> {
                { "username", config.Username.Value },
                { "password", config.Password.Value },
                { "submit", "come on in" }
            };

            var result = await RequestLoginAndFollowRedirect(LoginUrl, pairs, null, true, SiteLink, LoginUrl);
            ConfigureIfOK(result.Cookies, result.Content != null && result.Content.Contains("nav_profile"), () =>
            {
                CQ dom = result.Content;
                var messageEl = dom["#login_box_desc"];
                var errorMessage = messageEl.Text().Trim();
                throw new ExceptionWithConfigData(errorMessage, (ConfigurationData)config);
            });
        }

        public async Task<IEnumerable<ReleaseInfo>> PerformQuery(TorznabQuery query)
        {
            var releases = new List<ReleaseInfo>();
            var searchString = query.SanitizedSearchTerm + " " + query.GetEpisodeSearchString();
            var searchSection = string.IsNullOrEmpty(query.Episode) ? "archive" : "browse";
            var searchCategory = string.IsNullOrEmpty(query.Episode) ? "26" : "27";
            var searchUrl = string.Format(SearchUrl, searchSection, searchCategory, searchString);
            var results = await RequestStringWithCookiesAndRetry(searchUrl);

            try
            {
                CQ dom = results.Content;
                var rows = dom["#torrents-table > tbody > tr.tt_row"];
                foreach (var row in rows)
                {
                    CQ qRow = row.Cq();
                    var release = new ReleaseInfo();

                    release.MinimumRatio = 1;
                    release.MinimumSeedTime = 129600;
                    release.Title = qRow.Find(".ttr_name > a").Text();
                    release.Description = release.Title;
                    release.Guid = new Uri(SiteLink + "/" + qRow.Find(".ttr_name > a").Attr("href"));
                    release.Comments = release.Guid;
                    release.Link = new Uri(SiteLink + "/" + qRow.Find(".td_dl > a").Attr("href"));

                    var sizeStr = qRow.Find(".ttr_size").Contents()[0].NodeValue;
                    release.Size = ReleaseInfo.GetBytes(sizeStr);

                    var timeStr = qRow.Find(".ttr_added").Text();
                    DateTime time;
                    if (DateTime.TryParseExact(timeStr, "yyyy-MM-ddHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
                    {
                        release.PublishDate = time;
                    }

                    release.Seeders = ParseUtil.CoerceInt(qRow.Find(".ttr_seeders").Text());
                    release.Peers = ParseUtil.CoerceInt(qRow.Find(".ttr_leechers").Text()) + release.Seeders;

                    releases.Add(release);
                }
            }
            catch (Exception ex)
            {
                OnParseError(results.Content, ex);
            }

            return releases;
        }
    }
}
