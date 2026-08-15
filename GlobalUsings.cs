// Global using directives for BooksMetadataBaker

// Common System namespaces
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using System.Text;
global using System.Text.Json;
global using System.Text.RegularExpressions;
global using System.IO;
global using System.Security.Cryptography;

// Microsoft.Extensions namespaces
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Logging;
global using Microsoft.AspNetCore.Builder;
global using Microsoft.AspNetCore.Http;
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.AspNetCore.RateLimiting;

// BooksMetadataBaker namespaces
global using BooksMetadataBaker.Models;
global using BooksMetadataBaker.Services;
global using BooksMetadataBaker.Services.Abstract;
global using BooksMetadataBaker.Services.Integration;
global using BooksMetadataBaker.Startup;