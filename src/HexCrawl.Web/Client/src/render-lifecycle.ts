export type RenderCallback = () => void;
export type RenderScheduler = (callback: FrameRequestCallback) => number;

export class RenderLifecycle {
    private pending = false;
    private readonly renderers = new Map<string, RenderCallback>();

    public constructor(private readonly scheduler: RenderScheduler = callback => requestAnimationFrame(callback)) {}

    public register(name: string, renderer: RenderCallback): () => void {
        if (this.renderers.has(name)) throw new Error(`Renderer ${name} is already registered.`);
        this.renderers.set(name, renderer);
        return () => this.renderers.delete(name);
    }

    public requestRender(): void {
        if (this.pending) return;
        this.pending = true;
        this.scheduler(() => {
            this.pending = false;
            for (const renderer of this.renderers.values()) renderer();
        });
    }
}
