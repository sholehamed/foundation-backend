using System.Text;
using Scalar.ClientGeneration.Generation.Models;
using Scalar.ClientGeneration.Generation.TypeScript;

namespace Scalar.ClientGeneration.Generation.Angular;

internal sealed class AngularClientGenerator(
    TypeScriptTypeFormatter typeFormatter)
{
    public GeneratedSourceFile Generate(
        ClientApiDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(
            definition);

        var builder =
            new StringBuilder();

        WriteImports(
            builder,
            definition);

        builder.AppendLine();

        var className =
            TypeScriptNameHelper.ToPascalCase(
                definition.Group.Name)
            + "Client";

        builder.AppendLine("@Injectable({");
        builder.AppendLine("    providedIn: 'root'");
        builder.AppendLine("})");

        builder.AppendLine(
            $"export class {className} {{");

        builder.AppendLine();

        builder.AppendLine(
            "    private readonly http = inject(HttpClient);");

        foreach (var operation
                 in definition.Group.Operations)
        {
            builder.AppendLine();

            WriteOperation(
                builder,
                operation);
        }

        builder.AppendLine("}");

        var groupName =
            TypeScriptNameHelper.ToKebabCase(
                definition.Group.Name);

        return new GeneratedSourceFile(
            FileName:
                $"{groupName}.client.ts",

            Content:
                builder.ToString());
    }

    private void WriteImports(
        StringBuilder builder,
        ClientApiDefinition definition)
    {
        var hasQueryParameters =
            definition.Group.Operations
                .SelectMany(x => x.Parameters)
                .Any(x =>
                    x.Location ==
                    ClientParameterLocation.Query);

        if (hasQueryParameters)
        {
            builder.AppendLine(
                "import { HttpClient, HttpParams } from '@angular/common/http';");
        }
        else
        {
            builder.AppendLine(
                "import { HttpClient } from '@angular/common/http';");
        }

        builder.AppendLine(
            "import { Injectable, inject } from '@angular/core';");

        builder.AppendLine(
            "import { Observable } from 'rxjs';");

        var modelNames =
            definition.Models
                .Select(x => x.Name)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToArray();

        if (modelNames.Length == 0)
            return;

        builder.AppendLine();

        builder.AppendLine(
            "import {");

        foreach (var modelName
                 in modelNames)
        {
            builder.AppendLine(
                $"    {modelName},");
        }

        builder.AppendLine(
            $"}} from './{TypeScriptNameHelper.ToKebabCase(definition.Group.Name)}.models';");
    }

    private void WriteOperation(
        StringBuilder builder,
        ClientOperation operation)
    {
        var methodName =
            TypeScriptNameHelper.ToCamelCase(
                operation.Name);

        var methodParameters =
            BuildMethodParameters(
                operation);

        var responseType =
            typeFormatter.Format(
                operation.Response.Type);

        builder.AppendLine(
            $"    {methodName}(");

        for (var i = 0;
             i < methodParameters.Count;
             i++)
        {
            var parameter =
                methodParameters[i];

            var comma =
                i < methodParameters.Count - 1
                    ? ","
                    : string.Empty;

            builder.AppendLine(
                $"        {parameter}{comma}");
        }

        builder.AppendLine(
            $"    ): Observable<{responseType}> {{");

        builder.AppendLine();

        var url =
            BuildUrl(operation);

        builder.AppendLine(
            $"        const url = {url};");

        var queryParameters =
            operation.Parameters
                .Where(x =>
                    x.Location ==
                    ClientParameterLocation.Query)
                .ToArray();

        if (queryParameters.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine(
                "        let params = new HttpParams();");

            foreach (var queryParameter
                     in queryParameters)
            {
                WriteQueryParameter(
                    builder,
                    queryParameter);
            }
        }

        builder.AppendLine();

        WriteHttpCall(
            builder,
            operation,
            responseType,
            queryParameters.Length > 0);

        builder.AppendLine("    }");
    }

    private IReadOnlyList<string>
        BuildMethodParameters(
            ClientOperation operation)
    {
        var required =
            new List<string>();

        var optional =
            new List<string>();

        foreach (var parameter
                 in operation.Parameters)
        {
            //
            // فعلاً Header/Cookie را تولید نمی‌کنیم
            //
            if (parameter.Location
                is ClientParameterLocation.Header
                or ClientParameterLocation.Cookie)
            {
                continue;
            }

            var type =
                typeFormatter.Format(
                    parameter.Type);

            var name =
                TypeScriptNameHelper.Property(
                    parameter.Name);

            if (parameter.Required)
            {
                required.Add(
                    $"{name}: {type}");
            }
            else
            {
                optional.Add(
                    $"{name}?: {type}");
            }
        }

        if (operation.RequestBody
            is not null)
        {
            var bodyType =
                typeFormatter.Format(
                    operation.RequestBody.Type);

            if (operation.RequestBody.Required)
            {
                required.Add(
                    $"request: {bodyType}");
            }
            else
            {
                optional.Add(
                    $"request?: {bodyType}");
            }
        }

        return
        [
            .. required,
            .. optional
        ];
    }

    private static string BuildUrl(
        ClientOperation operation)
    {
        var path =
            operation.Path;

        var pathParameters =
            operation.Parameters
                .Where(x =>
                    x.Location ==
                    ClientParameterLocation.Path)
                .ToArray();

        if (pathParameters.Length == 0)
        {
            return
                $"'{Escape(path)}'";
        }

        foreach (var parameter
                 in pathParameters)
        {
            path =
                path.Replace(
                    $"{{{parameter.Name}}}",
                    $"${{encodeURIComponent(String({parameter.Name}))}}",
                    StringComparison.OrdinalIgnoreCase);
        }

        return
            $"`{path}`";
    }

    private static void WriteQueryParameter(
        StringBuilder builder,
        ClientParameter parameter)
    {
        var name =
            parameter.Name;

        if (parameter.Type.IsArray)
        {
            if (parameter.Required)
            {
                builder.AppendLine();
                builder.AppendLine(
                    $"        for (const value of {name}) {{");

                builder.AppendLine(
                    $"            params = params.append('{Escape(name)}', String(value));");

                builder.AppendLine(
                    "        }");
            }
            else
            {
                builder.AppendLine();
                builder.AppendLine(
                    $"        if ({name} !== undefined && {name} !== null) {{");

                builder.AppendLine(
                    $"            for (const value of {name}) {{");

                builder.AppendLine(
                    $"                params = params.append('{Escape(name)}', String(value));");

                builder.AppendLine(
                    "            }");

                builder.AppendLine(
                    "        }");
            }

            return;
        }

        builder.AppendLine();

        if (parameter.Required)
        {
            builder.AppendLine(
                $"        params = params.set('{Escape(name)}', String({name}));");
        }
        else
        {
            builder.AppendLine(
                $"        if ({name} !== undefined && {name} !== null) {{");

            builder.AppendLine(
                $"            params = params.set('{Escape(name)}', String({name}));");

            builder.AppendLine(
                "        }");
        }
    }

    private static void WriteHttpCall(
        StringBuilder builder,
        ClientOperation operation,
        string responseType,
        bool hasQuery)
    {
        var options =
            hasQuery
                ? "{ params }"
                : null;

        switch (
            operation.Method.ToUpperInvariant())
        {
            case "GET":

                builder.AppendLine(
                    hasQuery
                        ? $"        return this.http.get<{responseType}>(url, {options});"
                        : $"        return this.http.get<{responseType}>(url);");

                return;

            case "DELETE":

                builder.AppendLine(
                    hasQuery
                        ? $"        return this.http.delete<{responseType}>(url, {options});"
                        : $"        return this.http.delete<{responseType}>(url);");

                return;

            case "POST":

                WriteBodyRequest(
                    builder,
                    "post",
                    operation,
                    responseType,
                    hasQuery);

                return;

            case "PUT":

                WriteBodyRequest(
                    builder,
                    "put",
                    operation,
                    responseType,
                    hasQuery);

                return;

            case "PATCH":

                WriteBodyRequest(
                    builder,
                    "patch",
                    operation,
                    responseType,
                    hasQuery);

                return;

            default:

                WriteGenericRequest(
                    builder,
                    operation,
                    responseType,
                    hasQuery);

                return;
        }
    }

    private static void WriteBodyRequest(
        StringBuilder builder,
        string method,
        ClientOperation operation,
        string responseType,
        bool hasQuery)
    {
        var body =
            operation.RequestBody is null
                ? "null"
                : "request";

        if (hasQuery)
        {
            builder.AppendLine(
                $"        return this.http.{method}<{responseType}>(url, {body}, {{ params }});");
        }
        else
        {
            builder.AppendLine(
                $"        return this.http.{method}<{responseType}>(url, {body});");
        }
    }

    private static void WriteGenericRequest(
        StringBuilder builder,
        ClientOperation operation,
        string responseType,
        bool hasQuery)
    {
        var options =
            new List<string>();

        if (operation.RequestBody
            is not null)
        {
            options.Add(
                "body: request");
        }

        if (hasQuery)
        {
            options.Add(
                "params");
        }

        if (options.Count == 0)
        {
            builder.AppendLine(
                $"        return this.http.request<{responseType}>('{operation.Method}', url);");

            return;
        }

        builder.AppendLine(
            $"        return this.http.request<{responseType}>(");

        builder.AppendLine(
            $"            '{operation.Method}',");

        builder.AppendLine(
            "            url,");

        builder.AppendLine(
            "            {");

        foreach (var option
                 in options)
        {
            builder.AppendLine(
                $"                {option},");
        }

        builder.AppendLine(
            "            }");

        builder.AppendLine(
            "        );");
    }

    private static string Escape(
        string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("'", "\\'");
    }
}