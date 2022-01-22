using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Services;

namespace Azure.DataGateway.Service.Services
{
    /// <summary>
    /// Class which validates input supplied by a REST request.
    /// </summary>
    public class RequestValidator
    {
        /// <summary>
        /// Validates the given request by ensuring:
        /// - each field to be returned is one of the columns in the table.
        /// - extra fields specified in the body, will be discarded.
        /// </summary>
        /// <param name="context">Request context containing the REST operation fields and their values.</param>
        /// <param name="configurationProvider">Configuration provider that enables referencing DB schema in config.</param>
        /// <exception cref="DatagatewayException"></exception>
        public static void ValidateRequestContext(RestRequestContext context, IMetadataStoreProvider configurationProvider)
        {
            TableDefinition tableDefinition = TryGetTableDefinition(context.EntityName, configurationProvider);

            List<string> fieldsInRequest = new(context.FieldValuePairsInBody.Keys);

            foreach (string field in context.FieldsToBeReturned)
            {
                if (!tableDefinition.Columns.ContainsKey(field))
                {
                    throw new DatagatewayException(
                        message: "Invalid Column name requested: " + field,
                        statusCode: 400, DatagatewayException.SubStatusCodes.BadRequest);
                }
            }

            foreach (string field in fieldsInRequest)
            {
                if (!tableDefinition.Columns.ContainsKey(field))
                {
                    // TO DO: If the request header contains x-ms-must-match custom header,
                    // this should throw an error instead.
                    context.FieldValuePairsInBody.Remove(field);
                }
            }

            // Note: For insert operations,
            // if the field value pairs in the body do not contain values for all the primary keys
            // either they need to be auto-generated or the database would throw error.
            // It is possible to throw exception here before going to the database,
            // if we know the unspecified primary keys cannot be autogenerated.
        }

        /// <summary>
        /// Tries to validate the primary key in the request match those specified in the entity
        /// definition in the configuration file.
        /// </summary>
        /// <param name="context">Request context containing the primary keys and their values.</param>
        /// <param name="configurationProvider">Configuration provider that enables referencing DB schema in config.</param>
        /// <exception cref="DatagatewayException"></exception>
        public static void ValidatePrimaryKey(RestRequestContext context, IMetadataStoreProvider configurationProvider)
        {
            TableDefinition tableDefinition = TryGetTableDefinition(context.EntityName, configurationProvider);

            int countOfPrimaryKeysInSchema = tableDefinition.PrimaryKey.Count;
            int countOfPrimaryKeysInRequest = context.PrimaryKeyValuePairs.Count;

            if (countOfPrimaryKeysInRequest != countOfPrimaryKeysInSchema)
            {
                throw new DatagatewayException(
                    message: "Primary key column(s) provided do not match DB schema.",
                    statusCode: 400,
                    DatagatewayException.SubStatusCodes.BadRequest);
            }

            // Verify each primary key is present in the table definition.
            List<string> primaryKeysInRequest = new(context.PrimaryKeyValuePairs.Keys);
            IEnumerable<string> missingKeys = primaryKeysInRequest.Except(tableDefinition.PrimaryKey);

            if (missingKeys.Any())
            {
                throw new DatagatewayException(
                    message: $"The request is invalid since the primary keys: " +
                        string.Join(", ", missingKeys) +
                        " requested were not found in the entity definition.",
                        statusCode: 400,
                        DatagatewayException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Validates the request body and queryString with respect to an Insert operation.
        /// </summary>
        /// <param name="queryString">Query string from the url.</param>
        /// <param name="requestBody">The string JSON body from the request.</param>
        /// <exception cref="DatagatewayException"></exception>
        public static JsonElement ValidateInsertRequest(string queryString, string requestBody)
        {
            if (!string.IsNullOrEmpty(queryString))
            {
                throw new DatagatewayException(
                    message: "Query string for POST requests is an invalid url.",
                    statusCode: (int)HttpStatusCode.BadRequest,
                    subStatusCode: DatagatewayException.SubStatusCodes.BadRequest);
            }

            JsonElement insertPayloadRoot = new();

            if (!string.IsNullOrEmpty(requestBody))
            {
                using JsonDocument insertPayload = JsonDocument.Parse(requestBody);

                if (insertPayload.RootElement.ValueKind == JsonValueKind.Array)
                {
                    throw new NotSupportedException("InsertMany operations are not yet supported.");
                }
                else
                {
                    insertPayloadRoot = insertPayload.RootElement.Clone();
                }
            }

            return insertPayloadRoot;
        }

        /// <summary>
        /// Tries to get the table definition for the given entity from the configuration provider.
        /// </summary>
        /// <param name="entityName">Target entity name.</param>
        /// <param name="configurationProvider">Configuration provider that
        /// enables referencing DB schema in config.</param>
        /// <exception cref="DatagatewayException"></exception>

        private static TableDefinition TryGetTableDefinition(string entityName, IMetadataStoreProvider configurationProvider)
        {
            TableDefinition tableDefinition = configurationProvider.GetTableDefinition(entityName);
            if (tableDefinition == null)
            {
                throw new DatagatewayException(
                    message: $"TableDefinition for Entity: {entityName} does not exist.",
                    statusCode: (int)HttpStatusCode.BadRequest,
                    subStatusCode: DatagatewayException.SubStatusCodes.BadRequest);
            }

            return tableDefinition;
        }

    }
}
