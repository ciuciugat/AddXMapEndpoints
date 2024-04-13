using AutoMapper;
using AutoMapper.Internal;
using AutoMapper.QueryableExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System.Linq.Dynamic.Core;
using System.Reflection;
using System.Threading.Tasks;

namespace XMap
{

    #region Main Extension: WebApplicationXMapExtensions (contains: AddXMapEndpointsDto + AddXMapEndpoints)

    /// <summary>
    /// WebApplication Extensions: automatically create CRUD (and more) methods to manage EF entities and relative DTO's
    /// </summary>
    public static partial class WebApplicationXMapExtensions
    {
        /// <summary>
        /// WebApplication extension method: automatically create CRUD (and more) methods to manage EF Entities and DTO's
        /// </summary>
        /// <param name="app">WebApplication: Base type to extend</param>
        /// <param name="authorize">Manage [Authorize] attribute (yes/no)</param>
        public static void AddXMapEndpointsDto(this WebApplication app, bool authorize = false)
        {
            // Loop automapper DTO's marked with 'AddXMapEndpoints' attribute
            app.Services.GetRequiredService<IMapper>()
                .ConfigurationProvider.Internal().GetAllTypeMaps() // New Automapper libs ( >10.0 ) need .Internal()
                .Where(x => x.DestinationType.GetCustomAttributes(typeof(AddXMapEndpoints), true).Any()) // Filter only 'DestinationType' with 'AddXMapEndpoints' attribute
                .ToList().ForEach(x =>
                {
                    // Manage wether set [Authorize] attribute to API
                    if (authorize)
                    { 
                        app.MapGet("/XMap/"         + x.DestinationType.Name, [Authorize] async (DbContext db, IMapper mapper, HttpRequest request) => await GetListItemsDtoAsync(x, db, mapper, request));
                        app.MapGet("/XMapIQry/"     + x.DestinationType.Name, [Authorize] (DbContext db, IMapper mapper, HttpRequest request) => GetIQryItemsDto(x, db, mapper, request));
                        app.MapPut("/XMap/"         + x.DestinationType.Name, [Authorize] async (DbContext db, object json, IMapper mapper) => await PutDto(json, x, db, mapper));
                        app.MapPost("/XMap/"        + x.DestinationType.Name, [Authorize] async (DbContext db, object json, IMapper mapper) => await PostDto(json, x, db, mapper));
                        app.MapPost("/XMap/Delete/" + x.DestinationType.Name, [Authorize] async (DbContext db, object json, IMapper mapper) => await DeleteDto(json, x, db, mapper));
                        app.MapGet("/XMap/"         + x.DestinationType.Name + "/{key}",[Authorize] async (DbContext db, IMapper mapper, HttpRequest request) => await FindEntityDto(x, db, mapper, request));
                        app.MapPost("/XMap/QryObj/" + x.DestinationType.Name, [Authorize] async (DbContext db, object json, IMapper mapper) => await DoQueryDtoAsync(json, x, db, mapper));
                    }
                    else
                    {
                        // GET Async
                        app.MapGet("/XMap/" + x.DestinationType.Name, async (DbContext db, IMapper mapper, HttpRequest request) => await GetListItemsDtoAsync(x, db, mapper, request));

                        // GET Sync
                        app.MapGet("/XMapIQry/" + x.DestinationType.Name, (DbContext db, IMapper mapper, HttpRequest request) => GetIQryItemsDto(x, db, mapper, request));
                       
                        // PUT
                        app.MapPut("/XMap/" + x.DestinationType.Name, async (DbContext db, object json, IMapper mapper) => await PutDto(json, x, db, mapper));

                        // POST
                        app.MapPost("/XMap/" + x.DestinationType.Name, async (DbContext db, object json, IMapper mapper) => await PostDto(json, x, db, mapper));

                        // DELETE
                        app.MapPost("/XMap/Delete/" + x.DestinationType.Name, async (DbContext db, object json, IMapper mapper) => await DeleteDto(json, x, db, mapper));

                        // GET by: Key (multiple key => GetListItemsDtoAsync)
                        app.MapGet("/XMap/" + x.DestinationType.Name + "/{key}", async (DbContext db, IMapper mapper, HttpRequest request) => await FindEntityDto(x, db, mapper, request));

                        // GET by: Dynamic LINQ query
                        app.MapPost("/XMap/QryObj/" + x.DestinationType.Name, async (DbContext db, object json, IMapper mapper) => await DoQueryDtoAsync(json, x, db, mapper));

                        // OKOKOK IT WORKS but it's NOT the right way to work ;-)                       
                        //app.MapPost("/XMap/QryObj/" + x.DestinationType.Name, [Obsolete] async (DbContext db, object json, IMapper mapper) => await DoQueryDtoOkMaNo(json, x, db, mapper));

                    }
                });
        }

        #region CRUD + DoQuery Dto Methods

        /// <summary>
        /// Get an IQueryable of TDto object (TDto is x.DestinationType)
        /// </summary>
        /// <param name="x">Automapper TypeMap</param>
        /// <param name="db">EF Context instance</param>
        /// <param name="mapper">IMapper instance</param>
        /// <param name="request">Not needed (for future use)</param>
        /// <returns>IQueryable of TDto mapped (x.DestinationType)</returns>
        static IQueryable<object>? GetIQryItemsDto(TypeMap x, DbContext db, IMapper mapper, HttpRequest request)
        {            
            // Obtain 'DataDtoProvider' current type with 'ClassType' (GenericTypeArgument of DbSet inside DbContext)
            var dataProviderType = typeof(DataDtoProvider<,>).MakeGenericType(x.SourceType, x.DestinationType);

            // Create 'DataDtoProvider' instance (with current types)
            var dataProvider = Activator.CreateInstance(dataProviderType);

            // Get 'GetIQryItemsDto' method of 'DataDtoProvider'
            var mi = dataProviderType.GetMethods().FirstOrDefault(x => x.Name == "GetIQryItemsDto");

            // Check if attribute will load navigation of type 'Collection' (attr.IncludeChildren)                    
            var attr = x.DestinationType.GetCustomAttributes(typeof(AddXMapEndpoints), true).FirstOrDefault() as AddXMapEndpoints;

            // Invoke method to get IQueryable<T> (using 'ProjectTo' extension of AutoMapper)   
            object?[]? objs = { db, mapper, attr!.IncludeChildren };
            var iQry = mi?.Invoke(dataProvider, objs) as IQueryable<object>;

            // Result
            return iQry;
        }

        /// <summary>  
        /// Get a List of TDto (x.DestinationType)
        /// </summary>
        /// <param name="x">Automapper TypeMap</param>
        /// <param name="db">EF Context instance</param>
        /// <param name="mapper">IMapper instance</param>
        /// <param name="request">HttpRequest</param>
        /// <returns>List of TDto mapped (x.DestinationType)</returns>
        public static async Task<object> GetListItemsDtoAsync(TypeMap x, DbContext db, IMapper mapper, HttpRequest request)
        {
            // Multiple Keys management => FindEntityDto
            if (request.Query.Any())
                return await FindEntityDto(x, db, mapper, request);   

            // Obtain 'DataDtoProvider' current type with 'ClassType' (GenericTypeArgument of DbSet inside DbContext)
            var dataProviderType = typeof(DataDtoProvider<,>).MakeGenericType(x.SourceType, x.DestinationType);

            // Create 'DataDtoProvider' instance (with current types)
            var dataProvider = Activator.CreateInstance(dataProviderType);

            // Get 'GetIQryItemsDto' method of 'DataDtoProvider'
            var mi = dataProviderType.GetMethods().FirstOrDefault(x => x.Name == "GetListItemsDtoAsync");

            // Check if attribute will load navigation of type 'Collection' (attr.IncludeChildren)                    
            var attr = x.DestinationType.GetCustomAttributes(typeof(AddXMapEndpoints), true).FirstOrDefault() as AddXMapEndpoints;

            // Invoke method to get List<T> (enumerate IMMEDIATELY results, otherwise may get KAPUTT)
            object?[]? objs = { db, mapper, attr!.IncludeChildren };

            var list = await mi?.InvokeAsync(dataProvider, objs);

            // Return directly object
            return list;       
        }

        /// <summary>
        /// Get a TDto object (TDto is x.DestinationType)
        /// </summary>
        /// <param name="x">Automapper TypeMap</param>
        /// <param name="db">EF Context instance</param>
        /// <param name="mapper">IMapper instance</param>
        /// <param name="request">HttpRequest</param>
        /// <returns>TDto mapped (x.DestinationType)</returns>
        static async Task<object> FindEntityDto(TypeMap x, DbContext db, IMapper mapper, HttpRequest request)
        {
            // Single key
            var key = request.RouteValues["key"];

            // Multiple Keys management (take care to primitive type! StringValues must be converted as string)
            if (request.Query.Any())
            {
                var obj = new List<object>();
                request.Query.ToList().ForEach(x => obj.Add(x.Value.ToString()));
                key = obj.ToArray();
            }

            // Obtain 'DataDtoProvider' current type with 'ClassType' (GenericTypeArgument of DbSet inside DbContext)
            var dataProviderType = typeof(DataDtoProvider<,>).MakeGenericType(x.SourceType, x.DestinationType);

            // Create 'DataDtoProvider' instance (with current types)
            var dataProvider = Activator.CreateInstance(dataProviderType);

            // Get 'FindEntityDto' method of 'DataDtoProvider'
            var mi = dataProviderType.GetMethods().FirstOrDefault(x => x.Name == "FindEntityDto");

            // Check if attribute will load navigation of type 'Collection' (attr.IncludeChildren)                    
            var attr = x.DestinationType.GetCustomAttributes(typeof(AddXMapEndpoints), true).FirstOrDefault() as AddXMapEndpoints;

            // Invoke method to get IQueryable<T> (using 'ProjectTo' extension of AutoMapper)   
            object?[]? objs = { db, mapper, key, attr!.IncludeChildren };

            // Invoke method ASYNC to get <T> (using 'InvokeAsync' extension)  
            return await mi.InvokeAsync(dataProvider, objs);
        }

        /// <summary>
        /// Update a T object (T is x.SourceType)
        /// </summary>
        /// <param name="json">Serialized data</param>
        /// <param name="x">Automapper TypeMap</param>
        /// <param name="db">EF Context instance</param>
        /// <param name="mapper">IMapper instance</param>
        /// <returns>Updated TDto mapped (x.DestinationType)</returns>
        static async Task<object> PutDto(object json, TypeMap x, DbContext db, IMapper mapper)
        {
            // Entity passed is in JSON format (System.IO.JsonSerializer doesn't work properly !)
            var dto = JsonConvert.DeserializeObject(json?.ToString() ?? string.Empty, x.DestinationType);
            //var dto = JsonSerializer.Deserialize(json?.ToString(), x.DestinationType);
            var entity = mapper.Map(dto, x.DestinationType, x.SourceType);
            db.Update(entity);
            await db.SaveChangesAsync();

            // Return updated DTO
            dto=mapper.Map(entity, x.SourceType, x.DestinationType);
            return dto;
        }

        /// <summary>
        /// Create a T object (T is x.SourceType)
        /// </summary>
        /// <param name="json">Serialized data</param>
        /// <param name="x">Automapper TypeMap</param>
        /// <param name="db">EF Context instance</param>
        /// <param name="mapper">IMapper instance</param>
        /// <returns>Newly created TDto mapped (x.DestinationType)</returns>
        static async Task<object> PostDto(object json, TypeMap x, DbContext db, IMapper mapper)
        {
            var dto = JsonConvert.DeserializeObject(json?.ToString() ?? string.Empty, x.DestinationType);
            //var dto = JsonSerializer.Deserialize(json?.ToString(), x.DestinationType);
            var entity = mapper.Map(dto, x.DestinationType, x.SourceType);
            await db.AddAsync(entity);
            await db.SaveChangesAsync();

            // Return updated DTO
            dto = mapper.Map(entity, x.SourceType, x.DestinationType);
            return dto;
        }

        /// <summary>
        /// Delete a TDto object (T is x.SourceType)
        /// </summary>
        /// <param name="json">Serialized data</param>
        /// <param name="x">Automapper TypeMap</param>
        /// <param name="db">EF Context instance</param>
        /// <param name="mapper">IMapper instance</param>
        /// <returns>No content</returns>
        static async Task<IResult> DeleteDto(object json, TypeMap x, DbContext db, IMapper mapper)
        {
            // Trick.. use POST method to avoid passing entity 'keys' into URI (may be complex..)
            var dto = JsonConvert.DeserializeObject(json?.ToString() ?? string.Empty, x.DestinationType);
            //var dto = JsonSerializer.Deserialize(json?.ToString(), x.DestinationType);
            var entity = mapper.Map(dto, x.DestinationType, x.SourceType);
            db.Remove(entity);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }

        /// <summary>
        /// Get a List<![CDATA[]]>
        /// </summary>
        /// <param name="json">Serialized data</param>
        /// <param name="x">Automapper TypeMap</param>
        /// <param name="db">EF Context instance</param>
        /// <param name="mapper">IMapper instance</param>
        /// <returns>List of QryObj containing an IEnumerable of TDto mapped (x.DestinationType) object inside Result property.</returns>
        static async Task<object> DoQueryDtoAsync(object json, TypeMap x, DbContext db, IMapper mapper)
        {
            // Make generic QryObj<TDto> type
            var qryType = typeof(QryObj<>).MakeGenericType(x.DestinationType);

            // Get the QryObj instance
            var qry = JsonConvert.DeserializeObject(json?.ToString() ?? string.Empty, qryType);

            // Obtain 'DataDtoProvider' current type with 'ClassType' (GenericTypeArgument of DbSet inside DbContext)
            var dataProviderType = typeof(DataDtoProvider<,>).MakeGenericType(x.SourceType, x.DestinationType);

            // Create 'DataDtoProvider' instance (with current types)
            var dataProvider = Activator.CreateInstance(dataProviderType);

            // Get 'DoQueryDtoAsync' method of 'DataDtoProvider' 
            var mi = dataProviderType.GetMethods().FirstOrDefault(x => x.Name == "DoQueryDtoAsync");
            // Cannot use: nameof(DoQueryDtoAsync) it is another method, just names are the same
            // Cannot use: System.Reflection.MethodInfo.GetCurrentMethod() it is another method, just names are the same

            // Check if attribute will load navigation of type 'Collection' (attr.IncludeChildren)                    
            var attr = x.DestinationType.GetCustomAttributes(typeof(AddXMapEndpoints), true).FirstOrDefault() as AddXMapEndpoints;

            // Invoke method to get IQueryable<T> (using 'ProjectTo' extension of AutoMapper)   
            object?[]? objs = { db, mapper, qry, attr!.IncludeChildren };

            // Invokek method WITHOUT casting to any object (the caller knows th correct type)           
            var resQryObj = await mi?.InvokeAsync(dataProvider, objs);

            // Result
            return resQryObj;
        }

        #region OBSOLETE / Dsmissed/under test
        static async Task<object> DoQueryDtoOkMaNo(object json, TypeMap x, DbContext db, IMapper mapper)
        {
            // Make generic QryObj<TDto> type
            var qryType = typeof(QryObj<>).MakeGenericType(new[] { x.DestinationType });

            // Get the QryObj instance
            var qry = JsonConvert.DeserializeObject(json?.ToString(), qryType);

            // Obtain 'DataDtoProvider' current type with 'ClassType' (GenericTypeArgument of DbSet inside DbContext)
            var dataProviderType = typeof(DataDtoProvider<,>).MakeGenericType(new[] { x.SourceType, x.DestinationType });

            // Create 'DataDtoProvider' instance (with current types)
            var dataProvider = Activator.CreateInstance(dataProviderType);

            // Get 'DoQueryDto' method of 'DataDtoProvider'
            var mi = dataProviderType.GetMethods().FirstOrDefault(x => x.Name == "DoQueryDto");

            // Check if attribute will load navigation of type 'Collection' (attr.IncludeChildren)                    
            var attr = x.DestinationType.GetCustomAttributes(typeof(AddXMapEndpoints), true).FirstOrDefault() as AddXMapEndpoints;

            // Invoke method to get IQueryable<T> (using 'ProjectTo' extension of AutoMapper)   
            object?[]? objs = { db, mapper, qry, attr!.IncludeChildren };

            // Invokek method WITHOUT casting to any object (the caller knows th correct type)
            var resQryObj = mi?.Invoke(dataProvider, objs);

            // Result
            return Results.Ok(resQryObj);
        }

        static QryObj<object> DoQueryDto(object json, TypeMap x, DbContext db, IMapper mapper)
        {
            // Make generic QryObj<TDto> type
            var qryType = typeof(QryObj<>).MakeGenericType(new[] { x.DestinationType });

            // Get the QryObj instance
            var qry = JsonConvert.DeserializeObject(json?.ToString(), qryType);

            // Obtain 'DataDtoProvider' current type with 'ClassType' (GenericTypeArgument of DbSet inside DbContext)
            var dataProviderType = typeof(DataDtoProvider<,>).MakeGenericType(new[] { x.SourceType, x.DestinationType });

            // Create 'DataDtoProvider' instance (with current types)
            var dataProvider = Activator.CreateInstance(dataProviderType);

            // Get 'DoQueryDto' method of 'DataDtoProvider'
            var mi = dataProviderType.GetMethods().FirstOrDefault(x => x.Name == "DoQueryDto");

            // Check if attribute will load navigation of type 'Collection' (attr.IncludeChildren)                    
            var attr = x.DestinationType.GetCustomAttributes(typeof(AddXMapEndpoints), true).FirstOrDefault() as AddXMapEndpoints;

            // Invoke method to get IQueryable<T> (using 'ProjectTo' extension of AutoMapper)   
            object?[]? objs = { db, mapper, qry, attr!.IncludeChildren };

            // Invokek method WITHOUT casting to any object (the caller knows th correct type)
            var resQryObj = mi?.Invoke(dataProvider, objs) as QryObj<object>;

            // Result
            return resQryObj;
        }
        #endregion

        #endregion


        public static void AddXMapEndpoints(this WebApplication app, bool authorize = false)
        {
            // Loop Entities
            app.Services.CreateScope().ServiceProvider.GetRequiredService<DbContext>() // Get DBContext
                .Model.GetEntityTypes().Select(t => t.ClrType).ToList() // Enum Entities
                .ForEach(x =>
                 {
                     if (authorize)
                     {
                         app.MapGet("/XMap/" + x.Name, [Authorize] async (DbContext db, HttpRequest request) => await GetListItemsAsync(x, db, request));
                         app.MapGet("/XMapIQry/" + x.Name, [Authorize] (DbContext db, HttpRequest request) => GetIQueryItems(x, db, request));

                         app.MapGet("/XMap/" + x.Name + "/{key}", [Authorize] async (DbContext db, IMapper mapper, HttpRequest request) => await FindEntity(x, db, request));
                         app.MapPut("/XMap/" + x.Name, [Authorize] async (DbContext db, object json, IMapper mapper) => await Put(json, x, db));
                         app.MapPost("/XMap/" + x.Name, [Authorize] async (DbContext db, object json, IMapper mapper) => await Post(json, x, db));
                         app.MapPost("/XMap/Delete/" + x.Name, [Authorize] async (DbContext db, object json, IMapper mapper) => await Delete(json, x, db)); 
                         
                         app.MapPost("/XMap/QryObj/" + x.Name, [Authorize] async (DbContext db, object json, IMapper mapper) => await DoQueryAsync(json, x, db));

                     }
                     else
                     {
                         app.MapGet("/XMap/" + x.Name, async (DbContext db, HttpRequest request) => await GetListItemsAsync(x, db, request));
                         app.MapGet("/XMapIQry/" + x.Name, (DbContext db, HttpRequest request) => GetIQueryItems(x, db, request));

                         app.MapGet("/XMap/" + x.Name + "/{key}", async (DbContext db, IMapper mapper, HttpRequest request) => await FindEntity(x, db, request));
                         app.MapPut("/XMap/" + x.Name, async (DbContext db, object json, IMapper mapper) => await Put(json, x, db));
                         app.MapPost("/XMap/" + x.Name, async (DbContext db, object json, IMapper mapper) => await Post(json, x, db));
                         app.MapPost("/XMap/Delete/" + x.Name, async (DbContext db, object json, IMapper mapper) => await Delete(json, x, db));
                         app.MapPost("/XMap/QryObj/" + x.Name, async (DbContext db, object json, IMapper mapper) => await DoQueryAsync(json, x, db));
                     }
                 });

        }

        #region CRUD + DoQuery Methods
        public static IQueryable<object> GetIQueryItems(Type x, DbContext db, HttpRequest request)
        { 
            // Obtain 'DataProvider' current type with 'ClassType' (GenericTypeArgument of DbSet inside DbContext)
            var dataProviderType = typeof(DataProvider<>).MakeGenericType(x);

            // Create 'DataProvider' instance (with current types)
            var dataProvider = Activator.CreateInstance(dataProviderType);

            // Get 'GetIQryItems' method of 'DataProvider'
            var mi = dataProviderType.GetMethods().FirstOrDefault(x => x.Name == "GetIQryItems");

            // Invoke method to get IQueryable<T>
            object?[]? objs = { db };
            var iQry = mi?.Invoke(dataProvider, objs) as IQueryable<object>;

            // Result
            return iQry;
        }

        static async Task<object> GetListItemsAsync(Type x, DbContext db, HttpRequest request)
        {
            // Multiple Keys management => FindEntity
            if (request.Query.Any())
                return await FindEntity(x, db, request);

            // Obtain 'DataProvider' current type with 'ClassType'(GenericTypeArgument of DbSet inside DbContext)
            var dataProviderType = typeof(DataProvider<>).MakeGenericType(x);

            // Create 'DataProvider' instance (with current types)
            var dataProvider = Activator.CreateInstance(dataProviderType);

            // Get 'GetListItemsAsync' method of 'DataProvider'
            var mi = dataProviderType.GetMethods().FirstOrDefault(x => x.Name == "GetListItemsAsync");

            // Invoke method to get List<T> (enumerate IMMEDIATELY results, otherwise may get KAPUTT)
            object?[]? objs = { db };

            //var list = mi?.Invoke(dataProvider, objs) as List<object>;
            var list = await mi?.InvokeAsync(dataProvider, objs);

            // Return directly object
            return list;
        }

        static async Task<object> FindEntity(Type x, DbContext db, HttpRequest request)
        {
            var key = request.RouteValues["key"];

            // Obtain 'DataDtoProvider' current type with 'ClassType' (GenericTypeArgument of DbSet inside DbContext)
            var dataProviderType = typeof(DataProvider<>).MakeGenericType(x);

            // Create 'DataDtoProvider' instance (with current types)
            var dataProvider = Activator.CreateInstance(dataProviderType);

            // Get 'FindEntity' method of 'DataDtoProvider'
            var mi = dataProviderType.GetMethods().FirstOrDefault(x => x.Name == "FindEntity");

            // Invoke method to get IQueryable<T> (using 'ProjectTo' extension of AutoMapper)   
            object?[]? objs = { db, key };

            // Invoke method ASYNC to get <T> (using 'InvokeAsync' extension)  
            return await mi.InvokeAsync(dataProvider, objs);
        }

        static async Task<object> Put(object json, Type x, DbContext db)
        {
            var entity = JsonConvert.DeserializeObject(json?.ToString() ?? string.Empty, x);
            db.Update(entity);
            await db.SaveChangesAsync();
            return entity;
        }

        static async Task<object> Post(object json, Type x, DbContext db)
        {
            var entity = JsonConvert.DeserializeObject(json?.ToString() ?? string.Empty, x);
            await db.AddAsync(entity);
            await db.SaveChangesAsync();
            return entity;
        }

        static async Task<IResult> Delete(object json, Type x, DbContext db)
        {
            var entity = JsonConvert.DeserializeObject(json?.ToString() ?? string.Empty, x);
            db.Remove(entity);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }

        static async Task<object> DoQueryAsync(object json, Type x, DbContext db)
        {
            // Make generic QryObj<TDto> type
            var qryType = typeof(QryObj<>).MakeGenericType(x);

            // Get the QryObj instance
            var qry = JsonConvert.DeserializeObject(json?.ToString() ?? string.Empty, qryType);

            // Obtain 'DataProvider' current type with 'ClassType'(GenericTypeArgument of DbSet inside DbContext)
            var dataProviderType = typeof(DataProvider<>).MakeGenericType(x);

            // Create 'DataProvider' instance (with current types)
            var dataProvider = Activator.CreateInstance(dataProviderType);

            // Get 'DoQueryAsync' method of 'DataProvider'
            var mi = dataProviderType.GetMethods().FirstOrDefault(x => x.Name == "DoQueryAsync");
           
            // Invoke method to get IQueryable<T> (using 'ProjectTo' extension of AutoMapper)   
            object?[]? objs = { db, qry };

            // Invokek method WITHOUT casting to any object (the caller knows th correct type)           
            var resQryObj = await mi?.InvokeAsync(dataProvider, objs);

            // Result
            return resQryObj;
        }
        #endregion

    }
    #endregion




    #region DataDtoProvider => Provide data access with DTO layer

    /// <summary>
    /// DataDtoProvider: Class used to access EF data
    /// </summary>
    /// <typeparam name="T">EF entity type</typeparam>
    /// <typeparam name="TDto">DTO mapped type (Automapper)</typeparam>
    public class DataDtoProvider<T, TDto> where T : class, new()
    {
        /// <summary>
        /// Get a IQueryable object of TDto (NB: this methos is SYNC!)
        /// </summary>
        /// <param name="_context">EF Contex instance</param>
        /// <param name="mapper">Automapper instance</param>
        /// <param name="includeChildren">OBSOLETE: Commented after activating Lazy-Loading</param>
        /// <returns>IQueryable object of TDto</returns>
        public virtual IQueryable<TDto> GetIQryItemsDto(DbContext _context, IMapper mapper, bool includeChildren = false)
        {
            try
            {
                return _context.Set<T>()
                    //.WithInclude(includeChildren) // Commented after activating Lazy-Loading => Microsoft.EntityFrameworkCore.Proxies
                    .ProjectTo<TDto>(mapper.ConfigurationProvider);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        /// <summary>
        /// Get a List of TDto asynchronously
        /// </summary>
        /// <param name="_context">EF Contex instance</param>
        /// <param name="mapper">Automapper instance</param>
        /// <param name="includeChildren">OBSOLETE: Commented after activating Lazy-Loading</param>
        /// <returns>List of TDto</returns>
        public virtual async Task<List<TDto>> GetListItemsDtoAsync(DbContext _context, IMapper mapper, bool includeChildren = false)
        {
            try
            {
                var res = await _context.Set<T>()
                   //.WithInclude(includeChildren) // Commented after activating Lazy-Loading => Microsoft.EntityFrameworkCore.Proxies
                   .ToListAsync();
                var dtos= mapper.Map<List<TDto>>(res);
                return dtos;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        /// <summary>
        /// Find an entity with single or multiple Key(s)
        /// </summary>
        /// <param name="_context">EF Contex instance</param>
        /// <param name="mapper">Automapper instance</param>
        /// <param name="key">Single or multiple key(s) to find record</param>
        /// <param name="includeChildren">OBSOLETE: Commented after activating Lazy-Loading</param>
        /// <returns>Single TDto</returns>
        public virtual async Task<TDto>? FindEntityDto(DbContext _context, IMapper mapper, object key, bool includeChildren = false)
        {
            try
            {
                T? entity = default;
                var pk = _context.Model.FindEntityType(typeof(T))?.FindPrimaryKey();
                if (pk==null)
                    return default;

                if (key.GetType() == typeof(object[]))
                    // Multiple key
                    entity = await _context.Set<T>().FindAsync(key as object[]);                
                else 
                    // Single key (must be converted into the correct type)
                    entity = await _context.Set<T>().FindAsync(Convert.ChangeType(key, pk.Properties.First().ClrType));
                
                if (includeChildren)
                   await _context.Entry(entity)?.LoadNavigationsAsync();

                var dto = mapper.Map<TDto>(entity);

                var json = JsonConvert.SerializeObject(dto);
                //{ "Key1":"A","Key2":"B","Text":"AB"}

                return dto;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        /// <summary>
        /// Query EF Context DBSet using Dynamic LINQ
        /// </summary>
        /// <param name="_context">EF Contex instance</param>
        /// <param name="mapper">Automapper instance</param>
        /// <param name="qry">Query string (using Dynamic LINQ syntax)</param>
        /// <param name="includeChildren">OBSOLETE: Uncomment when Lazy-Loading is deactivated</param>
        /// <returns>List of QryObj containing IEnumerable of TDto (mapper.ConfigurationProvider)</returns>
        public virtual async Task<QryObj<TDto>> DoQueryDtoAsync(DbContext _context, IMapper mapper, QryObj<TDto> qry, bool includeChildren = false)
        {
            try
            {
                // QryObj example: var qry = new QryObj<Cat>() { Qry = "CatName==@0", Pars=new object[] { "Miu" } };

                // Exit when qry is null or empty
                if (string.IsNullOrWhiteSpace(qry.Qry)) return default;

                // Remove double quotes
                qry.Pars = qry.Pars?
                    .Select(x => System.Text.Json.JsonSerializer.Serialize<object>(x).Replace('"', ' ').Trim())
                    .ToArray();

                qry.Result= await _context.Set<T>()
                    //.WithInclude(includeChildren) // Commented after activating Lazy-Loading => Microsoft.EntityFrameworkCore.Proxies
                    .ProjectTo<TDto>(mapper.ConfigurationProvider)
                    .Where(qry.Qry, qry.Pars)
                    .ToListAsync();
              
                return qry;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        // Unuseful in remote scenarios: IQueryable interfaces.... DISMISSED!!
        /// <summary>
        /// Unuseful in remote scenarios: IQueryable interfaces.... DISMISSED!!
        /// </summary>
        /// <param name="_context">EF Contex instance</param>
        /// <param name="mapper">Automapper instance</param>
        /// <param name="qry">Query string (using Dynamic LINQ syntax)</param>
        /// <param name="includeChildren">OBSOLETE: Uncomment when Lazy-Loading is deactivated</param>
        /// <returns>QryObj containing IEnumerable of TDto (mapper.ConfigurationProvider)</returns>
        public virtual QryObj<TDto>? DoQueryDto(DbContext _context, IMapper mapper, QryObj<TDto> qry, bool includeChildren = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(qry.Qry)) return default;

                // Remove double quotes
                qry.Pars = qry.Pars?
                    .Select(x => System.Text.Json.JsonSerializer.Serialize<object>(x).Replace('"', ' ').Trim())
                    .ToArray();

                qry.Result=_context.Set<T>()
                    //.WithInclude(includeChildren) // Commented after activating Lazy-Loading => Microsoft.EntityFrameworkCore.Proxies
                    .ProjectTo<TDto>(mapper.ConfigurationProvider)
                    .Where(qry.Qry, qry.Pars);

                return qry;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }
    #endregion


    #region DataProvider => Provide direct data access to EF MODEL

    /// <summary>
    /// Provide data access to EF MODEL
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class DataProvider<T>  where T : class, new()
    {

        /// <summary>
        /// Get an IQueryable of T (NB: this mthod is SYNC)
        /// </summary>
        /// <param name="_context"></param>
        /// <returns>IQueryable of T</returns>
        public virtual IEnumerable<T> GetIQryItems(DbContext _context)
        {
            try
            {
                return _context.Set<T>()
                    .AsQueryable();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        /// <summary>
        /// Get a List of T Items async
        /// </summary>
        /// <param name="_context">EF context instance</param>
        /// <returns>List of T Items</returns>
        public virtual async Task<List<T>> GetListItemsAsync(DbContext _context)
        {
            try
            {
                return await _context
                    .Set<T>()
                    //.AsQueryable() // Now returns a LIST
                    .ToListAsync();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        /// <summary>
        /// Find an entity with single or multiple Key(s)
        /// </summary>
        /// <param name="_context"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        public virtual async Task<T>? FindEntity(DbContext _context, object key)
        {
            try
            {
                T? entity = default;
                var pk = _context.Model.FindEntityType(typeof(T))?.FindPrimaryKey() ?? default;
                if (pk==null)                
                    return default;
                
                if (key.GetType() == typeof(object[]))
                    // Multiple key
                    entity = await _context.Set<T>().FindAsync(key as object[]);
                else
                    // Single key (must be converted into the correct type)
                    entity = await _context.Set<T>().FindAsync(Convert.ChangeType(key, pk.Properties.First().ClrType));

                return entity;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        /// <summary>
        /// Query EF Context DBSet using Dynamic LINQ
        /// </summary>
        /// <param name="_context">EF context instance</param>
        /// <param name="qry">QryObj containing query string</param>
        /// <returns>QryObj containing IEnumerable of T (mapper.ConfigurationProvider)</returns>
        public virtual async Task<QryObj<T>>? DoQueryAsync(DbContext _context, QryObj<T> qry)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(qry.Qry)) return default;

                // Remove double quotes
                qry.Pars = qry.Pars?
                    .Select(x => System.Text.Json.JsonSerializer.Serialize<object>(x).Replace('"', ' ').Trim())
                    .ToArray();

                // Set Result => list of QryObj
                qry.Result= await _context.Set<T>()
                    .Where(qry.Qry, qry.Pars)
                    .ToListAsync();

                return qry;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

    }
    #endregion


    #region Useful Objects: calsses / atttributes
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class AddXMapEndpoints : Attribute
    {
        public AddXMapEndpoints(bool includeChildren = default)
        {
            this.IncludeChildren = includeChildren;
        }
        public bool IncludeChildren { get; set; }
    }

    public class QryObj<TDto>
    {
        public string? Qry { get; set; }
        public object[]? Pars { get; set; }

        public IEnumerable<TDto>? Result { get; set; }
    }
    #endregion


    #region Internal Extensions
    internal static partial class XMapExtensions
    {
        /// <summary>
        /// Estension of Method Info class that invoke itself in asynchronous mode
        /// </summary>
        /// <param name="mi">Method Info</param>
        /// <param name="obj">Method Info parent object</param>
        /// <param name="parameters">Method Info parameters</param>
        /// <returns>Result of method call</returns>
        public static async Task<object>? InvokeAsync(this MethodInfo mi, object obj, params object[] parameters)
        {
            var task = mi.Invoke(obj, parameters) as Task;
            if (task == null) return default;
            await task.ConfigureAwait(false);
            var resultProperty = task.GetType().GetProperty("Result");
            return resultProperty?.GetValue(task);

            //// Original code..
            //var task = (Task)mi.Invoke(obj, parameters);
            //await task.ConfigureAwait(false);
            //var resultProperty = task.GetType().GetProperty("Result");
            //return resultProperty.GetValue(task);
        }

        /// <summary>
        /// DBSet extension that 'loads' 'Navigation' properties 
        /// Many to 1 properties are always loaded
        /// 1 to many properties are loaded only when: includeChildren = true
        /// </summary>
        /// <typeparam name="T">Entity type</typeparam>
        /// <param name="dbSet">Extended class</param>
        /// <param name="includeChildren">Load 1 to many properties</param>
        /// <returns>IQueryable of T object</returns>
        public static IQueryable<T> WithInclude<T>(this DbSet<T> dbSet, bool includeChildren = false) where T : class, new()
        {
            try
            {
                var qry = dbSet.AsQueryable();
                if (includeChildren)
                {
                    // Get ALL navigations
                    dbSet.EntityType.GetNavigations()
                      .Select(x => x.PropertyInfo).ToList()
                      .ForEach(x => qry = qry.Include(x.Name));
                }
                else
                {
                    // Get only 'parent' relationship
                    dbSet.EntityType.GetNavigations()
                       .Where(x => !x.IsCollection)
                       .Select(x => x.PropertyInfo).ToList()
                       .ForEach(x => qry = qry.Include(x.Name));
                }

                // IQueryable<T> result
                return qry;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        /// <summary>
        /// Loads all 'Navigation' property asynchronously
        /// </summary>
        /// <param name="entry">Extended EF entity</param>
        /// <returns>Extended EF entity</returns>
        public static async Task LoadNavigationsAsync(this EntityEntry entry)
        {
            try
            {
                foreach (var v in entry.Navigations)                
                    await entry.Reference(v.Metadata.Name).LoadAsync();                
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        /// <summary>
        /// Get all DBSet of a EF DBContext
        /// </summary>
        /// <param name="context">EF context</param>
        /// <returns>List of Property Info (of type: DBSet)</returns>
        public static List<PropertyInfo> GetDbSetProperties(this DbContext context)
        {
            var dbSetProperties = new List<PropertyInfo>();
            var properties = context.GetType().GetProperties();

            foreach (var property in properties)
            {
                var setType = property.PropertyType;
                var isDbSet = setType.IsGenericType && typeof(DbSet<>).IsAssignableFrom(setType.GetGenericTypeDefinition());
                if (isDbSet)
                    dbSetProperties.Add(property);
            }
            return dbSetProperties;
        }

        // Expression<Func<TDTo, bool>> e1 = DynamicExpressionParser.ParseLambda<TDTo, bool>(null, true, "City = @0", "London");

    }
    #endregion

}
