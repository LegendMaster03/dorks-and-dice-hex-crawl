export type TilePolygonPoint = { x: number; y: number };

/** Common canvas path for authoritative realized tile polygons. */
export function traceTilePolygon(
    ctx: CanvasRenderingContext2D,
    vertices: readonly TilePolygonPoint[],
    project: (point: TilePolygonPoint) => TilePolygonPoint): void {
    ctx.beginPath();
    vertices.forEach((vertex, index) => {
        const point = project(vertex);
        if (index === 0) ctx.moveTo(point.x, point.y);
        else ctx.lineTo(point.x, point.y);
    });
    ctx.closePath();
}
