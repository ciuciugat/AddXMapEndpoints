# XMaps Enpoints (updated!) 

## Auto-generate Minimal API using a WebApplication extension.

### Working with DTO layer: MS Entity Framework + AutoMapper (recommended)

1)  Add the extension method inside your Program.cs class:

```
    app.AddXMapEndpointsDto();  // 'The Queen' said: It's a kind of magic :-)

    // If you are using Authorization/Authentication you can configure XMap with the specific overload:
    // app.AddXMapEndpointsDto(true); // Minimal API will be decorated with the [Authorize] attribute
```  

2) AutoMapper settings: ensure you declared an AutoMapper **profile** (it will be injected into API to map results).
 
 ```
    builder.Services.AddAutoMapper(typeof(YourAutoMapperProfile));
 ``` 

3) Decorate all the DTO for wich you want to create/read/update/delete with the **AddXMapEndpoints** attribute:

 ```
    [AddXMapEndpoints]

    public class MyDto
    {....}
 ```
Navigation properties (FK) are automatically loaded.

To load also **children** collection navigations you must explicitly declare them within the attribute:
`[AddXMapEndpoints(includeChildren: true)]`
 
**NB**: Alternatively it is possible to use **Lazy Loading** (installing: **Microsoft.EntityFrameworkCore.Proxies** package)


### Working directly with EF Models (no DTO layer):
Use this extension method to work directly with EF Models:
    
```
    app.AddXMapEndpoints();
    
    // app.AddXMapEndpoints(true); // Minimal API decorated with the [Authorize] attribute
```

The NET.CORE middleware will create all API endpoints for **every** Entity of your EF context.
  
**NB: Lazy Loading may cause problems! Take care to loops and children collection!**



### How it works: 

Decorating a DTO with the **AddXMapEndpoints** attribute will cause the NET.CORE middleware create those methods:

> GET 		=>	IEnumerable\<MyDto>

> GET 		=>	MyDto (searching by key)

> POST 		=>	MyDto

> PUT 		=>	MyDto


**New feature:  Do query using System.Linq.Dynamic.Core** 
> POST 	    => 	QryObj\<MyDto>


### The api, and relative routes, will be created in this way:

> MapGet	=>	"/XMap/" + typeof(MyDto).Name

> MapGet	=>	"/XMap/" + typeof(MyDto).Name 	+ "/{key}" 

> MapPost	=>	"/XMap/" + typeof(MyDto).Name	, myDto

> MapPut	=>	"/XMap/" + typeof(MyDto).Name	, myDto

> MapPost	=>	"/XMap/Delete/" + typeof(MyDto).Name	, myDto

**New feature: Do query using System.Linq.Dynamic.Core** 
> MapPost	=>	"/XMap/QryObj/" + typeof(MyDto).Name	, qryObj


  **NB:** *qryObj* is an object containing query definition (using **Dynamic.Linq syntax**, see examples below)

## Examples:

```
// Get a LIST of DTO
var many = await httpClient.GetFromJsonAsync<List<MyDto>>(baseUri + "/XMap/" + nameof(MyDto));

// Get a SINGLE DTO (with key = 1)
var one = await httpClient.GetFromJsonAsync<MyDto>(baseUri + "/XMap/" + nameof(MyDto) +"/1");

// Create a new entity (mapped from myDto)
var newDto = await httpClient.PostAsJsonAsync<MyDto>(baseUri + "/XMap/" + nameof(MyDto), myDto);

// Update an existing entity (mapped from myDto)
var updDto = await httpClient.PutAsJsonAsync<MyDto>(baseUri + "/XMap/" + nameof(MyDto), myDto);

// Delete an existing entity (mapped from myDto) NB: POST verb is used (to pass dto as body)
var res = await httpClient.PostAsJsonAsync<MyDto>(baseUri + "/XMap/Delete/" + nameof(MyDto), myDto);

// Manage multiple keyed Dto (Key1 and Key2 are PK fields)
var list = await httpClient.GetFromJsonAsync<List<DoubleKeyedDto>>(baseUri + "XMap/" + nameof(DoubleKeyedDto));
// Get item by keys (pass them into uri: ?key1=A&key2=B)
var item = await httpClient.GetFromJsonAsync<DoubleKeyedDto>(baseUri + "XMap/" + nameof(DoubleKeyedDto)+"?key1=A&key2=B");
```


**Examples with EF Modles (without DTO layer)** 
```
// Type names will determine route rules: MyEntity instead of MyDto (take care to type names!)
var many = await httpClient.GetFromJsonAsync<List<MyEntity>>(baseUri + "/XMap/" + nameof(MyEntity));
var one = await httpClient.GetFromJsonAsync<MyEntity>(baseUri + "/XMap/" + nameof(MyEntity) +"/1");
var newone = await httpClient.PostAsJsonAsync<MyEntity>(baseUri + "/XMap/" + nameof(MyEntity), myEntity);
var upd = await httpClient.PutAsJsonAsync<MyEntity>(baseUri + "/XMap/" + nameof(MyEntity), myEntity);
var res = await httpClient.PostAsJsonAsync<MyEntity>(baseUri + "/XMap/Delete/" + nameof(MyEntity), myEntity);
...
```

**New feature: Do query using System.Linq.Dynamic.Core** 
```
// Query database dynamically with QryObj class (in this case 'ParentDtoProperty' is a property mapped using a Navigation)
var qry = new QryObj<MyDto>() { Qry = "ParentDtoProperty==@0", Pars = new object[] { "some value" } }; 

// POST verb is used (to pass QryObj as body)
var qryres = await httpClient.PostAsJsonAsync<QryObj<MyDto>>(baseUri + "/XMap/QryObj/" + nameof(MyDto), qry);

// Read content
string jsonContent = await qryres.Content.ReadAsStringAsync();

// Get QryObj deserializing content
var obj = JsonConvert.DeserializeObject<QryObj<MyDto>>(jsonContent);

// 'Result' property contains the collection of IEnumerable<MyDto>
var myDtoCollection = obj?.Result;
```

**QryObj Class definition (XMap namespace)** 
```

namespace XMap
{
    public class QryObj<TDto>
    {
        public string Qry { get; set; }
        public object[] Pars { get; set; }

        public IEnumerable<TDto> Result { get; set; }
    }
    .....
}

```

