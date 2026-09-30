import { proxyToApi } from "@/server/api-proxy";

/** Route parameters of the data proxy. */
interface ProxyContext {
  params: Promise<{ path: string[] }>;
}

/** Forwards the request to the API, whatever the method. */
async function handle(request: Request, context: ProxyContext): Promise<Response> {
  return proxyToApi(request, (await context.params).path);
}

/** Forwards a read to the API. */
export const GET = handle;

/** Forwards a create or an action to the API. */
export const POST = handle;

/** Forwards an update to the API. */
export const PUT = handle;

/** Forwards a partial update to the API. */
export const PATCH = handle;

/** Forwards a delete to the API. */
export const DELETE = handle;
