using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;

using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;

using Newtonsoft.Json;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace AWSLambdaExample
{
    public class Functions
    {
        // This const is the name of the environment variable that the serverless.template will use to set
        // the name of the DynamoDB table used to store music posts.
        const string TABLENAME_ENVIRONMENT_VARIABLE_LOOKUP = "MusicTable";

        public const string ID_QUERY_STRING_NAME = "Artist"; // Hash Key
        public const string RANGE_QUERY_STRING_NAME = "SongTitle"; // Range Key

        IDynamoDBContext DDBContext { get; set; }

        /// <summary>
        /// Default constructor that Lambda will invoke.
        /// </summary>
        public Functions()
        {
            // Check to see if a table name was passed in through environment variables and if so 
            // add the table mapping.
            var tableName = System.Environment.GetEnvironmentVariable(TABLENAME_ENVIRONMENT_VARIABLE_LOOKUP);
            if(!string.IsNullOrEmpty(tableName))
            {
                AWSConfigsDynamoDB.Context.TypeMappings[typeof(Music)] = new Amazon.Util.TypeMapping(typeof(Music), tableName);
            }

            var config = new DynamoDBContextConfig { Conversion = DynamoDBEntryConversion.V2 };
            this.DDBContext = new DynamoDBContext(new AmazonDynamoDBClient(), config);
        }

        /// <summary>
        /// Constructor used for testing passing in a preconfigured DynamoDB client.
        /// </summary>
        /// <param name="ddbClient"></param>
        /// <param name="tableName"></param>
        public Functions(IAmazonDynamoDB ddbClient, string tableName)
        {
            if (!string.IsNullOrEmpty(tableName))
            {
                AWSConfigsDynamoDB.Context.TypeMappings[typeof(Music)] = new Amazon.Util.TypeMapping(typeof(Music), tableName);
            }

            var config = new DynamoDBContextConfig { Conversion = DynamoDBEntryConversion.V2 };
            this.DDBContext = new DynamoDBContext(ddbClient, config);
        }

        /// <summary>
        /// A Lambda function that returns back a page worth of music posts.
        /// </summary>
        /// <param name="request"></param>
        /// <returns>The list of musics</returns>
        public async Task<APIGatewayProxyResponse> GetMusicsAsync(APIGatewayProxyRequest request, ILambdaContext context)
        {
            context.Logger.LogLine("Getting musics");
            var search = this.DDBContext.ScanAsync<Music>(null);
            var page = await search.GetNextSetAsync();
            context.Logger.LogLine($"Found {page.Count} musics");

            var response = new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.OK,
                Body = JsonConvert.SerializeObject(page),
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };

            return response;
        }

        /// <summary>
        /// A Lambda function that returns the music identified by Artist and SongTitle
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public async Task<APIGatewayProxyResponse> GetMusicAsync(APIGatewayProxyRequest request, ILambdaContext context)
        {
            string Artist = null;
            if (request.PathParameters != null && request.PathParameters.ContainsKey(ID_QUERY_STRING_NAME))
            { 
                Artist = request.PathParameters[ID_QUERY_STRING_NAME];
            }
            else if (request.QueryStringParameters != null && request.QueryStringParameters.ContainsKey(ID_QUERY_STRING_NAME))
            { 
                Artist = request.QueryStringParameters[ID_QUERY_STRING_NAME];
            }

            if (string.IsNullOrEmpty(Artist))
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                    Body = $"Missing required parameter {ID_QUERY_STRING_NAME}"
                };
            }

            string SongTitle = null;
            if (request.PathParameters != null && request.PathParameters.ContainsKey(RANGE_QUERY_STRING_NAME))
            {
                SongTitle = request.PathParameters[RANGE_QUERY_STRING_NAME];
            }
            else if (request.QueryStringParameters != null && request.QueryStringParameters.ContainsKey(RANGE_QUERY_STRING_NAME))
            {
                SongTitle = request.QueryStringParameters[RANGE_QUERY_STRING_NAME];
            }

            if (string.IsNullOrEmpty(SongTitle))
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                    Body = $"Missing required parameter {RANGE_QUERY_STRING_NAME}"
                };
            }


            context.Logger.LogLine($"Getting music {Artist}, {SongTitle}");
            var music = await DDBContext.LoadAsync<Music>(Artist, SongTitle);
            context.Logger.LogLine($"Found music: {music != null}");

            if (music == null)
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.NotFound
                };
            }

            var response = new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.OK,
                Body = JsonConvert.SerializeObject(music),
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };
            return response;
        }

        /// <summary>
        /// A Lambda function that adds a music post.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public async Task<APIGatewayProxyResponse> AddMusicAsync(APIGatewayProxyRequest request, ILambdaContext context)
        {
            var music = JsonConvert.DeserializeObject<Music>(request?.Body);
            music.CreatedTimestamp = DateTime.Now;

            context.Logger.LogLine($"Saving music Artist: {music.Artist}, SongTitle: {music.SongTitle}.");
            await DDBContext.SaveAsync<Music>(music);

            var response = new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.OK,
                Body = $"Saving music Artist: {music.Artist}, SongTitle: {music.SongTitle}.",
                Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
            };
            return response;
        }

        /// <summary>
        /// A Lambda function that removes a music post from the DynamoDB table.
        /// </summary>
        /// <param name="request"></param>
        public async Task<APIGatewayProxyResponse> RemoveMusicAsync(APIGatewayProxyRequest request, ILambdaContext context)
        {
            string Artist = null;
            if (request.PathParameters != null && request.PathParameters.ContainsKey(ID_QUERY_STRING_NAME))
            {
                Artist = request.PathParameters[ID_QUERY_STRING_NAME];
            }
            else if (request.QueryStringParameters != null && request.QueryStringParameters.ContainsKey(ID_QUERY_STRING_NAME))
            {
                Artist = request.QueryStringParameters[ID_QUERY_STRING_NAME];
            }

            if (string.IsNullOrEmpty(Artist))
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                    Body = $"Missing required parameter {ID_QUERY_STRING_NAME}"
                };
            }

            string SongTitle = null;
            if (request.PathParameters != null && request.PathParameters.ContainsKey(RANGE_QUERY_STRING_NAME))
            {
                SongTitle = request.PathParameters[RANGE_QUERY_STRING_NAME];
            }
            else if (request.QueryStringParameters != null && request.QueryStringParameters.ContainsKey(RANGE_QUERY_STRING_NAME))
            {
                SongTitle = request.QueryStringParameters[RANGE_QUERY_STRING_NAME];
            }

            if (string.IsNullOrEmpty(SongTitle))
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                    Body = $"Missing required parameter {RANGE_QUERY_STRING_NAME}"
                };
            }

            context.Logger.LogLine($"Deleting music Artist: {Artist}, SongTitle: {SongTitle}.");
            await this.DDBContext.DeleteAsync<Music>(Artist, SongTitle);

            return new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.OK
            };
        }
    }
}
