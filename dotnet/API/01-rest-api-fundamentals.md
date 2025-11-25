# Chapter 1: REST API Fundamentals & HTTP Basics

## 1.1 HTTP Protocol Essentials

### HTTP Methods (Verbs)

HTTP defines standardized methods that indicate the intended action on a resource. In API design, these carry semantic meaning.

**GET** - Retrieve data without side effects
- Safe: does not modify server state
- Idempotent: same request returns same result
- Cacheable: responses can be cached
- Must not have a request body (though technically allowed, it's ignored)
- Example: `GET /api/users/123`

**POST** - Create a new resource or trigger an action
- Not safe: modifies server state
- Not idempotent: multiple requests create multiple resources
- Cacheable: only if explicit cache headers present
- Has a request body containing the data to create
- Example: `POST /api/users` with user data in body

**PUT** - Replace an entire resource with the provided representation
- Not safe: modifies server state
- Idempotent: multiple identical requests result in same state
- Request body contains complete resource representation
- Returns 200 (OK) or 204 (No Content) on success
- Should replace the entire resource, not partial updates
- Example: `PUT /api/users/123` with complete user object

**PATCH** - Partially update a resource
- Not safe: modifies server state
- May or may not be idempotent (depends on implementation)
- Request body contains only the fields to update
- More flexible than PUT for partial updates
- Standard format uses JSON Merge Patch or JSON Patch
- Example: `PATCH /api/users/123` with `{ "email": "new@example.com" }`

**DELETE** - Remove a resource
- Not safe: modifies server state
- Idempotent: deleting an already-deleted resource should return same result
- Should return 204 (No Content) on success
- Example: `DELETE /api/users/123`

**HEAD** - Like GET but without response body
- Used to check resource existence or get headers without downloading body
- Less common in modern APIs

**OPTIONS** - Describe communication options for a resource
- Used by browsers for CORS preflight requests
- Can list allowed methods for a resource

---

### HTTP Status Codes

Status codes are three-digit numbers indicating the outcome of a request. They're grouped into five classes:

**2xx Success Codes** - Request succeeded as expected

- **200 OK** - Request succeeded, response contains data
  - Use for: GET requests, POST/PUT returning data
  - Include response body with the resource or result

- **201 Created** - Resource was successfully created
  - Use for: POST requests that create resources
  - Must include `Location` header with URI of new resource
  - Include response body with created resource representation

- **204 No Content** - Request succeeded but no content to return
  - Use for: DELETE operations, successful operations with no return data
  - Must not include response body

- **202 Accepted** - Request accepted for processing but not completed
  - Use for: Long-running operations, async processing
  - Helps client understand operation is still processing

**4xx Client Error Codes** - Client provided invalid request

- **400 Bad Request** - Server cannot process the request
  - Use for: Invalid request format, missing required fields, validation errors
  - Include details about what was invalid

- **401 Unauthorized** - Authentication required or failed
  - Use for: Missing credentials, invalid credentials, expired tokens
  - Include `WWW-Authenticate` header indicating auth scheme

- **403 Forbidden** - Authenticated but not authorized to access resource
  - Use for: User lacks permissions, insufficient role/claims
  - Client is authenticated but denied access

- **404 Not Found** - Resource does not exist
  - Use for: Requested resource doesn't exist at given URI
  - Must not reveal whether resource existed before

- **409 Conflict** - Request conflicts with current state
  - Use for: Duplicate creation attempts, version conflicts, race conditions
  - Common with optimistic concurrency control

- **422 Unprocessable Entity** - Request format valid but semantically incorrect
  - Use for: Business logic violations, domain validation failures
  - Distinct from 400: syntax is fine, but meaning is invalid

**5xx Server Error Codes** - Server failed to fulfill valid request

- **500 Internal Server Error** - Unexpected server error
  - Use for: Unhandled exceptions, bugs
  - Client cannot determine cause; should retry or contact support

- **503 Service Unavailable** - Server temporarily cannot handle request
  - Use for: Maintenance, overload, database connection issues
  - Include `Retry-After` header suggesting when to retry

---

### HTTP Headers

Headers provide metadata about requests and responses.

**Request Headers (Common)**

- **Authorization** - Credentials for authentication
  - Format: `Authorization: Bearer <token>` or `Authorization: Basic <credentials>`
  - Required for protected endpoints

- **Content-Type** - Media type of request body
  - Example: `Content-Type: application/json`
  - Tells server how to parse body

- **Accept** - Media types client can handle in response
  - Example: `Accept: application/json`
  - Enables content negotiation

- **User-Agent** - Information about client making request
  - Example: `User-Agent: MyApp/1.0 (.NET 7.0)`

**Response Headers (Common)**

- **Content-Type** - Media type of response body
  - Must match what client requested via Accept header

- **Location** - URI of newly created resource (201 Created)
  - Client uses this to access created resource

- **Cache-Control** - Caching directives
  - Example: `Cache-Control: max-age=3600, public`
  - Controls whether and how long response is cached

- **ETag** - Entity tag for cache validation
  - Hash of resource content
  - Client includes in `If-None-Match` to check if changed

- **Set-Cookie** - Sets cookie on client
  - Less common in APIs; prefer Bearer tokens

---

## 1.2 REST Principles and Constraints

REST (Representational State Transfer) is an architectural style, not a standard. It's defined by six constraints:

### Client-Server Architecture
- Client and server are independent
- Server doesn't need to know about client's UI
- Enables evolution of each independently
- Communication through standardized protocol (HTTP)

### Statelessness
- Each request contains all information needed to understand and process it
- Server doesn't store client context between requests
- Improves scalability (can route to any server)
- Enables horizontal scaling without session affinity

**Example:**
```
// Good: includes credentials every request
GET /api/users/123
Authorization: Bearer eyJhbGc...

// Bad: relies on session from previous request
Request 1: POST /api/login (stores session)
Request 2: GET /api/users/123 (relies on stored session)
```

### Uniform Interface
The API presents a consistent, predictable interface:

**1. Resource Identification in Requests**
- Resources are identified by URIs
- Each resource has a unique URI
- Representation sent in request/response, not the resource itself

Example: `/api/users/123` identifies user resource 123

**2. Resource Manipulation Through Representations**
- Client manipulates resources through their representations
- Representation includes enough data to modify/delete
- Server changes resource based on representation received

**3. Self-Descriptive Messages**
- Each message contains information needed to understand it
- Includes media type, HTTP method, status code
- No reliance on out-of-band information

**4. Hypermedia As The Engine Of Application State (HATEOAS)**
- Response includes links to related resources
- Client discovers available actions through links
- Reduces coupling between client and server
- Often omitted in modern APIs for simplicity

Example with HATEOAS:
```json
{
  "id": 123,
  "name": "John Doe",
  "_links": {
    "self": { "href": "/api/users/123" },
    "all-users": { "href": "/api/users" },
    "delete": { "href": "/api/users/123", "method": "DELETE" }
  }
}
```

### Cacheability
- Responses labeled as cacheable or non-cacheable
- Reduces need for server requests
- Improves perceived performance
- Implementers must honor caching directives

Example caching:
```
GET /api/users/123
Response:
Cache-Control: max-age=3600  // cached for 1 hour
ETag: "abc123xyz"            // for validation
```

### Layered System
- Architecture composed of hierarchical layers
- Each layer only knows about adjacent layer
- Enables scalability (add caching layers, load balancers, gateways)
- Client unaware of whether connected directly to server

---

## 1.3 API Design Best Practices

### Resource-Oriented Design

APIs should be designed around **resources**, not actions.

**Resource-Oriented (Good)**
```
POST   /api/users              Create user
GET    /api/users              List users
GET    /api/users/123          Get user 123
PUT    /api/users/123          Update user 123
DELETE /api/users/123          Delete user 123
```

**Action-Oriented (Anti-pattern)**
```
POST /api/createUser
POST /api/getUser?id=123
POST /api/updateUser
POST /api/deleteUser?id=123
```

Action-oriented APIs:
- Violate REST principles
- Don't use HTTP methods semantically
- Harder to cache (everything is POST)
- More endpoints to maintain

### URI Design

**Use nouns, not verbs**
```
Good:  GET /api/users/123/orders
Bad:   GET /api/users/123/getOrders
Bad:   GET /api/getOrders?userId=123
```

**Use hierarchical structure for relationships**
```
GET /api/users/123/orders           - orders for user 123
GET /api/users/123/orders/456       - order 456 for user 123
GET /api/users/123/orders/456/items - items in order 456
```

**Use lowercase and hyphens for multi-word resources**
```
Good:  /api/user-profiles, /api/order-items
Bad:   /api/UserProfiles, /api/orderItems, /api/user_profiles
```

**Use query parameters for filtering, not path segments**
```
Good:  GET /api/orders?status=pending&limit=10
Bad:   GET /api/orders/pending/10
```

**Avoid deep nesting (typically max 2-3 levels)**
```
Good:  /api/users/123/orders
Bad:   /api/tenants/1/departments/2/teams/3/users/4/orders/5
```

For complex hierarchies, flatten using IDs:
```
GET /api/users/123/orders       - user's orders
GET /api/orders?userId=123      - same, alternative approach
GET /api/orders/456             - order 456 (no need for user in path)
```

### Versioning Strategy

Choose a versioning approach early and stick with it:

**URL Path Versioning** (most common)
```
GET /api/v1/users
GET /api/v2/users
```
Pros: Clear, easy to test
Cons: Creates multiple paths for same resource

**Header Versioning**
```
GET /api/users
Accept: application/vnd.myapi.v2+json
```
Pros: Cleaner URIs
Cons: Less obvious, harder to test in browser

**Query Parameter Versioning**
```
GET /api/users?api-version=2
```
Pros: Easy to test
Cons: Mixes API version with query concerns

### Error Response Format

Standardize error responses. Use **RFC 7807 Problem Details**:

```json
{
  "type": "https://api.example.com/errors/validation-error",
  "title": "Validation Error",
  "status": 422,
  "detail": "One or more validation errors occurred.",
  "traceId": "00-abc123-def456-00",
  "errors": {
    "email": ["Email is required", "Email format is invalid"],
    "age": ["Age must be 18 or older"]
  }
}
```

Provides:
- `type`: URI describing error type (for documentation)
- `title`: Human-readable title
- `status`: HTTP status code
- `detail`: Specific error details
- Additional fields as needed (like `errors` above)

---

## 1.4 Request/Response Lifecycle

Understanding the full flow helps design better APIs:

1. **Client initiates request**
   - Constructs URL, HTTP method, headers, body
   - Includes authentication credentials

2. **Server receives request**
   - Routes to appropriate handler based on method and URI
   - Parses headers, body, query parameters

3. **Authentication**
   - Validates credentials (token, API key, etc.)
   - Returns 401 if invalid

4. **Authorization**
   - Checks if authenticated user has permissions
   - Returns 403 if insufficient permissions

5. **Input Validation**
   - Validates request format and data
   - Returns 400 if invalid
   - Returns 422 if valid format but business logic violation

6. **Business Logic Processing**
   - Executes operation (fetch, create, update, delete)
   - May call databases, external services
   - Transaction handling if needed

7. **Response Preparation**
   - Formats response (JSON, XML, etc.)
   - Sets appropriate status code
   - Adds relevant headers (Location, ETag, Cache-Control)

8. **Client receives response**
   - Parses status code to understand outcome
   - Handles errors or processes returned data
   - May cache if cache headers present

---

## 1.5 Content Negotiation

Content negotiation allows clients and servers to agree on response format.

**Accept Header**
```
GET /api/users/123
Accept: application/json
```

Server responds with appropriate Content-Type:
```
Content-Type: application/json
```

**Multiple Formats**
```
Accept: application/json, application/xml;q=0.9, */*;q=0.1
```
Indicates preference: JSON (1.0), XML (0.9), anything else (0.1)

**Common Media Types for APIs**
- `application/json` - JSON format (most common)
- `application/xml` - XML format (legacy systems)
- `application/problem+json` - RFC 7807 error responses
- `application/hal+json` - JSON with HATEOAS links

---

## 1.6 Common API Patterns

### Collection vs. Resource

**Collection endpoint** returns multiple resources:
```
GET /api/users
Response:
[
  { "id": 1, "name": "Alice" },
  { "id": 2, "name": "Bob" }
]
```

**Resource endpoint** returns single resource:
```
GET /api/users/1
Response:
{ "id": 1, "name": "Alice" }
```

### Creating Resources

Standard pattern for creation:
```
POST /api/users
Request body:
{
  "name": "Charlie",
  "email": "charlie@example.com"
}

Response: 201 Created
Location: /api/users/3
{
  "id": 3,
  "name": "Charlie",
  "email": "charlie@example.com"
}
```

Always return created resource so client knows its new ID.

### Bulk Operations

Options for updating multiple resources:

**Option 1: Multiple requests**
```
PATCH /api/users/1
PATCH /api/users/2
```
Simple but inefficient for many resources

**Option 2: Bulk endpoint**
```
PATCH /api/users/bulk
[
  { "id": 1, "status": "active" },
  { "id": 2, "status": "inactive" }
]
```
More efficient but requires separate endpoint design

**Option 3: Filter-based**
```
PATCH /api/users?status=pending
{ "status": "active" }
```
Updates all matching resources

---

## Summary

REST APIs are built on HTTP fundamentals: methods express intent, status codes communicate outcomes, and headers provide metadata. Resource-oriented design using proper URIs, consistent error handling, and content negotiation create predictable, maintainable APIs. The remaining chapters will show how to implement these principles in ASP.NET Core.
