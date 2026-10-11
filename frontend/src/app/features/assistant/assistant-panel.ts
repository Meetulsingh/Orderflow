import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, ElementRef, effect, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AssistantApi } from '../../core/api.service';
import { AuthService } from '../../core/auth/auth.service';
import { CartStore } from '../../core/cart.store';
import { problemMessage } from '../../core/error.interceptor';
import { Product } from '../../core/models';
import { Icon } from '../../shared/icon';
import { ProductArt } from '../../shared/product-art';

interface Message {
  role: 'user' | 'assistant';
  content: string;
  products?: Product[];
  error?: boolean;
}

const SUGGESTIONS = ['Gift ideas under $60', 'What do I need to start running?', 'Best-rated electronics', 'Something cozy for home'];
const MAX_HISTORY = 12;

/** Floating AI shopping assistant: asks the backend, which searches the catalog with tools and returns product cards. */
@Component({
  selector: 'app-assistant-panel',
  imports: [FormsModule, RouterLink, Icon, ProductArt, CurrencyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (enabled()) {
      @if (!open()) {
        <button class="fab" (click)="open.set(true)" aria-label="Open shopping assistant">
          <app-icon name="sparkles" [size]="18" /> <span>Ask AI</span>
        </button>
      } @else {
        <aside class="panel" role="dialog" aria-label="Shopping assistant">
          <header>
            <span class="badge-ai"><app-icon name="sparkles" [size]="16" /></span>
            <div class="grow">
              <div class="strong">Shopping assistant</div>
              <div class="xs muted">AI · searches the live catalog</div>
            </div>
            @if (messages().length > 0) {
              <button class="btn btn-ghost btn-sm" (click)="reset()" title="Start over">New chat</button>
            }
            <button class="btn btn-ghost btn-icon btn-sm" (click)="open.set(false)" aria-label="Close assistant"><app-icon name="x" [size]="16" /></button>
          </header>

          <div class="body" #scroller>
            @if (!auth.isAuthenticated()) {
              <div class="intro">
                <p>Sign in to chat with the AI shopping assistant. It finds products in the catalog and adds them to your cart in one click.</p>
                <a routerLink="/login" class="btn btn-primary btn-sm" (click)="open.set(false)"><app-icon name="login" [size]="15" /> Sign in</a>
              </div>
            } @else {
              @if (messages().length === 0) {
                <div class="intro">
                  <p>Hi{{ firstName() }}! Tell me what you're looking for and I'll search the catalog for you.</p>
                  <div class="chips">
                    @for (s of suggestions; track s) {
                      <button class="chip" (click)="send(s)">{{ s }}</button>
                    }
                  </div>
                </div>
              }

              @for (m of messages(); track $index) {
                <div class="msg" [class.user]="m.role === 'user'" [class.error]="m.error">
                  <div class="bubble">{{ m.content }}</div>
                </div>
                @if (m.products?.length) {
                  <div class="cards">
                    @for (p of m.products; track p.id) {
                      <div class="card-p">
                        <div class="thumb"><app-product-art [name]="p.name" [category]="p.category" [imageUrl]="p.imageUrl" [iconSize]="20" /></div>
                        <div class="grow info">
                          <div class="strong name">{{ p.name }}</div>
                          <div class="xs muted">{{ p.price | currency }} · ★ {{ p.rating }}</div>
                        </div>
                        @if (p.stock === 0) {
                          <span class="xs muted">Sold out</span>
                        } @else {
                          <button class="btn btn-sm" [class.btn-primary]="!added().has(p.id)" (click)="addToCart(p)">
                            @if (added().has(p.id)) { <app-icon name="check" [size]="14" /> Added } @else { <app-icon name="plus" [size]="14" /> Add }
                          </button>
                        }
                      </div>
                    }
                  </div>
                }
              }

              @if (thinking()) {
                <div class="msg"><div class="bubble typing"><span></span><span></span><span></span></div></div>
              }
            }
          </div>

          @if (auth.isAuthenticated()) {
            <form class="composer" (ngSubmit)="send(draft)">
              <input class="input" name="draft" [(ngModel)]="draft" placeholder="e.g. a gift for a coffee lover" maxlength="500" autocomplete="off" [disabled]="thinking()" />
              <button class="btn btn-primary btn-icon" type="submit" [disabled]="thinking() || !draft.trim()" aria-label="Send"><app-icon name="arrow-right" [size]="16" /></button>
            </form>
          }
        </aside>
      }
    }
  `,
  styles: `
    .fab {
      position: fixed; right: 20px; bottom: 20px; z-index: 80;
      display: inline-flex; align-items: center; gap: 8px; padding: 12px 18px; border: 0; border-radius: 999px; cursor: pointer;
      color: #fff; font-weight: 650; font-size: 0.92rem;
      background: linear-gradient(135deg, var(--primary), var(--accent)); box-shadow: var(--shadow-lg);
      transition: transform 0.15s var(--ease);
      &:hover { transform: translateY(-2px); }
    }
    .panel {
      position: fixed; right: 20px; bottom: 20px; z-index: 80;
      width: min(400px, calc(100vw - 32px)); height: min(600px, calc(100vh - 100px));
      display: flex; flex-direction: column; overflow: hidden;
      background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-lg); box-shadow: var(--shadow-lg);
      animation: rise 0.25s var(--ease);
    }
    @keyframes rise { from { opacity: 0; transform: translateY(12px); } }
    header { display: flex; align-items: center; gap: 10px; padding: 12px 12px 12px 16px; border-bottom: 1px solid var(--border); }
    .badge-ai { width: 32px; height: 32px; border-radius: 10px; display: grid; place-items: center; color: #fff; background: linear-gradient(135deg, var(--primary), var(--accent)); }
    .body { flex: 1; overflow-y: auto; padding: 16px; display: flex; flex-direction: column; gap: 10px; background: var(--surface-2); }
    .intro { display: flex; flex-direction: column; gap: 12px; color: var(--text-2); font-size: 0.9rem; }
    .chips { display: flex; flex-wrap: wrap; gap: 8px; }
    .chip {
      padding: 7px 12px; border-radius: 999px; border: 1px solid var(--border-strong); background: var(--surface);
      color: var(--text); font-size: 0.82rem; cursor: pointer;
      &:hover { border-color: var(--primary); color: var(--primary); }
    }
    .msg { display: flex; }
    .msg.user { justify-content: flex-end; }
    .bubble {
      max-width: 85%; padding: 9px 13px; border-radius: 14px; font-size: 0.9rem; line-height: 1.45; white-space: pre-wrap;
      background: var(--surface); border: 1px solid var(--border); color: var(--text);
    }
    .msg.user .bubble { background: var(--primary); border-color: var(--primary); color: #fff; }
    .msg.error .bubble { background: var(--danger-50); border-color: var(--danger-50); color: var(--danger); }
    .cards { display: flex; flex-direction: column; gap: 8px; }
    .card-p { display: flex; align-items: center; gap: 10px; padding: 8px; border: 1px solid var(--border); border-radius: var(--radius); background: var(--surface); }
    .thumb { width: 44px; height: 44px; border-radius: 10px; overflow: hidden; flex-shrink: 0; }
    .info { min-width: 0; }
    .name { font-size: 0.86rem; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .typing { display: inline-flex; gap: 4px; }
    .typing span { width: 6px; height: 6px; border-radius: 50%; background: var(--text-3); animation: blink 1.2s infinite; }
    .typing span:nth-child(2) { animation-delay: 0.2s; }
    .typing span:nth-child(3) { animation-delay: 0.4s; }
    @keyframes blink { 0%, 80%, 100% { opacity: 0.25; } 40% { opacity: 1; } }
    .composer { display: flex; gap: 8px; padding: 12px; border-top: 1px solid var(--border); }
    .composer .input { flex: 1; }
    @media (max-width: 640px) {
      .panel { right: 8px; left: 8px; bottom: 8px; width: auto; height: calc(100vh - 80px); }
      .fab { right: 16px; bottom: 16px; }
    }
  `
})
export class AssistantPanel {
  private readonly api = inject(AssistantApi);
  private readonly cart = inject(CartStore);
  protected readonly auth = inject(AuthService);
  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');

  protected readonly suggestions = SUGGESTIONS;
  protected readonly enabled = signal(false);
  protected readonly open = signal(false);
  protected readonly messages = signal<Message[]>([]);
  protected readonly thinking = signal(false);
  protected readonly added = signal(new Set<string>());
  protected draft = '';

  constructor() {
    // Only show the assistant when the deployment has a model key configured.
    this.api.status().subscribe({ next: s => this.enabled.set(s.enabled), error: () => this.enabled.set(false) });

    // Keep the latest message in view.
    effect(() => {
      this.messages();
      this.thinking();
      const el = this.scroller()?.nativeElement;
      if (el) queueMicrotask(() => el.scrollTo({ top: el.scrollHeight, behavior: 'smooth' }));
    });
  }

  protected firstName(): string {
    const name = this.auth.user()?.fullName.split(' ')[0];
    return name ? ` ${name}` : '';
  }

  send(text: string) {
    const content = text.trim();
    if (!content || this.thinking()) return;
    this.draft = '';
    this.messages.update(m => [...m, { role: 'user', content }]);
    this.thinking.set(true);

    const history = this.messages()
      .filter(m => !m.error)
      .slice(-MAX_HISTORY)
      .map(({ role, content }) => ({ role, content }));

    this.api.chat(history).subscribe({
      next: r => {
        this.messages.update(m => [...m, { role: 'assistant', content: r.reply, products: r.products }]);
        this.thinking.set(false);
      },
      error: err => {
        this.messages.update(m => [...m, { role: 'assistant', content: problemMessage(err), error: true }]);
        this.thinking.set(false);
      }
    });
  }

  addToCart(product: Product) {
    this.cart.add(product);
    this.added.update(s => new Set(s).add(product.id));
  }

  reset() {
    this.messages.set([]);
    this.added.set(new Set());
  }
}
