using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using APIInterface.SupportingClasses;
using System.Net;
using PhyloDivas.Data.New.Entities;
using static System.Net.WebRequestMethods;
using System.Text.Json;

namespace APIInterface
{
    public class APICalls
    {

        private Databasemanagement management = new Databasemanagement();

        private readonly HttpClient client;
        //Base URL Stays the Same
        private string BaseURL = "https://api.ncbi.nlm.nih.gov";
     
        public APICalls() 
        { 
            client = new HttpClient();
            client.BaseAddress = new Uri(BaseURL);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            //client.DefaultRequestHeaders.Add("api-key", add api key here);
        }
        //create dictionary based on generics
        //read object then create objects


        public async Task<string> MakeCallAsync(HttpMethod method, string endpoint, Dictionary<string, string> headers = null, Dictionary<string, string> queryParams = null, string body = null)
        {
            try
            {
                if (queryParams != null && queryParams.Count > 0)
                {
                    string query = string.Join("&", queryParams.Select(kvp => kvp.Key + "=" + Uri.EscapeDataString(kvp.Value)));
                    if (endpoint.Contains("?"))
                    {
                        endpoint = endpoint + "&" + query;
                    }
                    else
                    {
                        endpoint = endpoint + "?" + query;
                    }
                }

                HttpRequestMessage request = new HttpRequestMessage(method, new Uri(client.BaseAddress, endpoint));

                if (headers != null)
                {
                    foreach (KeyValuePair<string, string> kvp in headers)
                    {
                        request.Headers.Add(kvp.Key, kvp.Value);
                    }
                }

                if (body != null && method == HttpMethod.Post)
                {
                    request.Content = new StringContent(body, Encoding.UTF8, "applicaiton/json");
                }

                HttpResponseMessage response = await client.SendAsync(request);
                string responseContent = await response.Content.ReadAsStringAsync();
                return responseContent;
            }
            catch (Exception ex)
            {

                management.AddError(ex.TargetSite.ToString(), ex.StackTrace, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Calls the NCBI Databanks Restful API to return genome data by ID
        /// </summary>
        /// <param name="genomeIDs">A list of Genome Ids</param>
        /// <returns>a GenomeResponse object with a report for each genome ID passed in.</returns>
        public async Task<GenomeResponse> GetGenomeByIDs(List<string> genomeIDs)
        {
            //add query param
            //filters.
            //assembly_source enum refseq

            try
            {
                string accessions = "";
                foreach (string genomeID in genomeIDs)
                {
                    accessions += genomeID + "%2C";
                }
                accessions = accessions.Remove(accessions.Length - 3);
                string endpoint = "/datasets/v2/genome/accession/" + accessions + "/dataset_report";
                Dictionary<string,string> queryParams = new Dictionary<string,string>();
                queryParams.Add("filters.assembly_source", "refseq");
                string responseJson = await MakeCallAsync(HttpMethod.Get, endpoint,null,queryParams);

                if (string.IsNullOrEmpty(responseJson) || responseJson.StartsWith("Exception"))
                {
                    management.AddError(endpoint,"Get",responseJson);
                    return null;
                }
                //before returning grab and add to database.
                GenomeResponse returnObject = DeserializeGenomeJson(responseJson);
                try
                {


                    foreach (GenomeReport genomeReport in returnObject.Reports)
                    {
                        management.AddReport(genomeReport);
                    }
                    return returnObject;
                }
                catch (Exception ex) {
                    return returnObject;
                }
            }
            catch (Exception ex)
            {

                management.AddError(ex.TargetSite.ToString(), ex.StackTrace, ex.Message);
                return null;
            }
        }


        /// <summary>
        /// Calls the NCBI Databanks Restful API to return gene data by ID
        /// </summary>
        /// <param name="Accessions">A list of Gene Accessions</param>
        /// <returns>a GeneResponse object with a report for each gene ID passed in.</returns>
        public async Task<GeneResponse> GetGeneByAccessions(List<string> Accessions)
        {
            try
            {
                string accessions = "";
                foreach (string accession in Accessions) 
                {
                    accessions += accession + "%2C";
                }
                accessions = accessions.Remove(accessions.Length - 3);
                string endpoint = "/datasets/v2/gene/accession/"+ accessions +"/dataset_report";
                string responseJson = await MakeCallAsync(HttpMethod.Get,endpoint);
                if (string.IsNullOrEmpty(responseJson) || responseJson.StartsWith("Exception"))
                {
                    management.AddError(endpoint, "Get", responseJson);
                    return null;
                }
                GeneResponse returnObject = DeserializeGeneJson(responseJson);
                try
                {


                    foreach (GeneReport geneReport in returnObject.Reports)
                    {
                        management.AddReport(geneReport.Gene);
                    }
                    return returnObject;
                }
                catch {
                    return returnObject;
                }
                return returnObject;
            }
            catch (Exception ex) 
            {
                //when the error logging table is implemented add error logging
                management.AddError(ex.TargetSite.ToString(), ex.StackTrace, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Calls the NCBI Databanks Restful API to return taxon suggestions based on the string passed in.
        /// </summary>
        /// <param name="suggestion">string the suggestions will be based on</param>
        /// <returns>a TaxonSuggest Response which contains a list of suggestions in order from best to worst fit.</returns>
        public async Task<TaxonSuggestResponse> GetTaxonsBySuggestion(string suggestion)
        {
            try
            {
                string endpoint = "/datasets/v2/taxonomy/taxon_suggest/" + suggestion;
                string responseJson = await MakeCallAsync(HttpMethod.Get, endpoint);
                if (string.IsNullOrEmpty(responseJson) || responseJson.StartsWith("Exception"))
                {
                    management.AddError(endpoint, "Get", responseJson);
                    return null;
                }
                TaxonSuggestResponse returnObject = DeserializeTaxonSuggestJson(responseJson);
                try
                {


                    foreach (SciNameAndId addObject in returnObject.SciNameAndIds)
                    {
                        management.AddReport(addObject);
                    }
                    return returnObject;
                }
                catch (Exception ex) 
                { 
                    return returnObject;
                }
            }
            catch (Exception ex)
            {
                //when the error logging table is implemented add error logging
                management.AddError(ex.TargetSite.ToString(), ex.StackTrace, ex.Message);
                return null;
            }
        }


        /// <summary>
        /// Calls the NCBI Databanks Restful API to return genomes based on taxonomy.
        /// </summary>
        /// <param name="Taxons">list of strings containing either the common name, scientific name, or taxon id.</param>
        /// <returns>A Genome response object will be returned with a report for each taxon in the list of taxons</returns>
        public async Task<GenomeResponse> GetGenomeByTaxons(List<string> Taxons)
        {
            //add query param
            //filters.
            //assembly_source enum refseq

            try
            {
                string taxons = "";
                foreach (string taxon in Taxons)
                {
                    taxons +=  taxon + "%2C";
                }
                taxons = taxons.Remove(taxons.Length - 3);
                string endpoint = "/datasets/v2/genome/taxon/" + taxons + "/dataset_report";
                Dictionary<string, string> queryParams = new Dictionary<string, string>();
                queryParams.Add("filters.assembly_source", "refseq");
                string responseJson = await MakeCallAsync(HttpMethod.Get, endpoint, null, queryParams);

                if (string.IsNullOrEmpty(responseJson) || responseJson.StartsWith("Exception"))
                {
                    management.AddError(endpoint, "Get", responseJson);
                    return null;
                }
                GenomeResponse returnObject = DeserializeGenomeJson(responseJson);
                try
                {
                    foreach (GenomeReport genomeReport in returnObject.Reports)
                    {
                        management.AddReport(genomeReport);
                    }
                    return returnObject;
                }
                catch (Exception ex) {
                    return returnObject;
                }
            }
            catch (Exception ex)
            {
                management.AddError(ex.TargetSite.ToString(), ex.StackTrace, ex.Message);
                return null;
            }
        }



        /// <summary>
        /// Calls the NCBI Databanks Restful API to return genes based on taxonomy.
        /// </summary>
        /// <param name="Taxons">list of strings containing either the common name, scientific name, or taxon id.</param>
        /// <returns>A Gene response object will be returned with a report for each gene associated with the taxons</returns>
        public async Task<GeneResponse> GetGeneByTaxons(List<string> Taxons)
        {
            try
            {
                string organisms = "";
                foreach (string taxon in Taxons)
                {
                    organisms += taxon + "%2C";
                }
                organisms = organisms.Remove(organisms.Length - 3);
                string endpoint = "/datasets/v2/gene/taxon/" + organisms + "/dataset_report";
                string responseJson = await MakeCallAsync(HttpMethod.Get, endpoint);
                if (string.IsNullOrEmpty(responseJson) || responseJson.StartsWith("Exception"))
                {
                    management.AddError(endpoint, "Get", responseJson);
                    return null;
                }
                GeneResponse returnObject = DeserializeGeneJson(responseJson);
                try
                {


                    foreach (GeneReport addObject in returnObject.Reports)
                    {
                        management.AddReport(addObject.Gene);
                    }

                    return returnObject;
                }
                catch (Exception ex) {
                    return returnObject;
                }
            }
            catch (Exception ex)
            {
                //when the error logging table is implemented add error logging
                management.AddError(ex.TargetSite.ToString(), ex.StackTrace, ex.Message);
                return null;
            }
        }



        /// <summary>
        /// Calls the NCBI Databanks Restful API to return gene orthologs based on a specific gene Id.
        /// </summary>
        /// <param name="geneId">a gene Id</param>
        /// <returns>A Gene response object will be returned with a report for each ortholog associated with the passed in gene Id</returns>
        public async Task<GeneResponse> GetGeneOrthologsByGeneId(int geneId)
        {
            try
            {
                string endpoint = "/datasets/v2/gene/id/" + geneId + "/orthologs";
                string responseJson = await MakeCallAsync(HttpMethod.Get, endpoint);
                if (string.IsNullOrEmpty(responseJson) || responseJson.StartsWith("Exception"))
                {
                    management.AddError(endpoint, "Get", responseJson);
                    return null;
                }
                GeneResponse returnObject = DeserializeGeneJson(responseJson);
                try
                {


                    foreach (GeneReport addObject in returnObject.Reports)
                    {
                        management.AddReport(addObject.Gene);
                    }
                    return returnObject;
                }catch (Exception ex) { return returnObject; }
            }
            catch (Exception ex)
            {
                //when the error logging table is implemented add error logging
                management.AddError(ex.TargetSite.ToString(), ex.StackTrace, ex.Message); ;
                return null;
            }
        }


        /// <summary>
        /// Calls the NCBI Databanks Restful API to return genes based on taxonomy and a list of symbols.
        /// </summary>
        /// <param name="Symbols">a list of Symbols that are associated with a gene.</param>
        /// <param name="Taxon">common name, scientific name, or taxon id.</param>
        /// <returns>A Genome response object will be returned with a report for each gene associated with the taxons</returns>
        public async Task<GeneResponse> GetGeneBySymbolsandTaxon(List<string> Symbols, string Taxon)
        {
            try
            {
                string symbols = "";
                foreach (string symbol in Symbols)
                {
                    symbols += symbol + "%2C";
                }
                symbols = symbols.Remove(symbols.Length - 3);
                string endpoint = "/datasets/v2/gene/symbol/" + symbols + "/taxon/"+ Taxon +"/dataset_report";
                string responseJson = await MakeCallAsync(HttpMethod.Get, endpoint);
                if (string.IsNullOrEmpty(responseJson) || responseJson.StartsWith("Exception"))
                {
                    management.AddError(endpoint, "Get", responseJson);
                    return null;
                }
                GeneResponse geneObject = DeserializeGeneJson(responseJson);
                try
                {
                    if (geneObject == null || geneObject.Reports == null)
                    {
                        return null;
                    }

                    foreach (GeneReport addObject in geneObject.Reports)
                    {
                        management.AddReport(addObject.Gene);
                    }
                    return geneObject;
                }
                catch
                {
                    return geneObject;
                }
            }
            catch (Exception ex)
            {
                //when the error logging table is implemented add error logging
                management.AddError(ex.TargetSite.ToString(), ex.StackTrace, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Calls the NCBI Databanks Restful API to return genes based on gene Ids.
        /// </summary>
        /// <param name="GeneIds">list of integers containing gene ids.</param>
        /// <returns>A Gene response object will be returned with a report for each gene associated with the ids passed in</returns>
        public async Task<GeneResponse> GetGeneByGeneIds(List<int> GeneIds)
        {
            try
            {
                string geneIds = "";
                foreach (int geneId in GeneIds)
                {
                    geneIds += geneId + "%2C";
                }
                geneIds = geneIds.Remove(geneIds.Length - 3);
                string endpoint = "/datasets/v2/gene/id/" + geneIds + "/dataset_report";
                string responseJson = await MakeCallAsync(HttpMethod.Get, endpoint);
                if (string.IsNullOrEmpty(responseJson) || responseJson.StartsWith("Exception"))
                {
                    management.AddError(endpoint, "Get", responseJson);
                    return null;
                }
                GeneResponse geneResponse = DeserializeGeneJson(responseJson);
                try
                {


                    foreach (GeneReport addObject in geneResponse.Reports)
                    {
                        management.AddReport(addObject.Gene);
                    }
                    return geneResponse;
                }
                catch (Exception ex) { return geneResponse; }
            }
            catch (Exception ex)
            {
                //when the error logging table is implemented add error logging
                management.AddError(ex.TargetSite.ToString(), ex.StackTrace, ex.Message);
                return null;
            }
        }

        public async Task<int?> GetTaxonIdByCommonName(string commonName)
        {
            try
            {
                string query = commonName.Trim();
                string endpoint = "/datasets/v2/taxonomy/taxon/" + query + "?returned_content=TAXIDS";
                string responseJson = await MakeCallAsync(HttpMethod.Get, endpoint);

                if (string.IsNullOrEmpty(responseJson) || responseJson.StartsWith("Exception"))
                {
                    management.AddError(endpoint, "Get", responseJson);
                    return null;
                }
                
                TaxonomyResponse taxonomyResponse = DeserializeTaxonomyJson(responseJson);

                if (taxonomyResponse?.TaxonomyNodes != null && taxonomyResponse.TaxonomyNodes.Count > 0)
                {
                    if (taxonomyResponse?.TaxonomyNodes?[0].Taxonomy != null)
                    {
                        return taxonomyResponse.TaxonomyNodes[0].Taxonomy.TaxId;
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }
            }
            catch (Exception ex)
            {
                management.AddError(ex.TargetSite.ToString(), ex.StackTrace, ex.Message);
                return null;
            }
        }

        public async Task<bool> CheckWithinTaxon(int geneId, int taxonId)
        {
            try
            {
                string url = $"https://eutils.ncbi.nlm.nih.gov/entrez/eutils/esummary.fcgi?db=gene&id={geneId}&retmode=json";

                HttpResponseMessage response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    // Optional: log status code
                    Console.WriteLine($"Failed to fetch gene {geneId}: {response.StatusCode}");
                    return false;
                }

                string jsonResult = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(jsonResult);
                var root = doc.RootElement;

                // E-utilities wraps the result inside "result" → GeneID
                if (root.TryGetProperty("result", out JsonElement resultElement) &&
                    resultElement.TryGetProperty(geneId.ToString(), out JsonElement geneElement) &&
                    geneElement.TryGetProperty("taxid", out JsonElement taxidElement))
                {
                    int geneTaxId = taxidElement.GetInt32();
                    return geneTaxId == taxonId;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public TaxonSuggestResponse DeserializeTaxonSuggestJson(string json)
        {
            return JsonConvert.DeserializeObject<TaxonSuggestResponse>(json);
        }

        public GeneResponse DeserializeGeneJson(string json)
        {
            return JsonConvert.DeserializeObject<GeneResponse>(json);
        }

        public GenomeResponse DeserializeGenomeJson(string jsonString)
        {
            return JsonConvert.DeserializeObject<GenomeResponse>(jsonString);
        }

        public TaxonomyResponse DeserializeTaxonomyJson(string json)
        {
            return JsonConvert.DeserializeObject<TaxonomyResponse>(json);
        }
    }
}
