import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Subject, Subscription, debounceTime, distinctUntilChanged, finalize } from 'rxjs';
import { CatalogApi } from '../../core/api.service';
import { retryWhileWaking } from '../../core/warmup.service';
import { AuthService } from '../../core/auth/auth.service';
import { CartStore } from '../../core/cart.store';
import { Category, Product } from '../../core/models';
import { ToastService } from '../../core/toast.service';
import { Icon } from '../../shared/icon';
import { ProductArt } from '../../shared/product-art';
import { ProductForm } from './product-form';

const PAGE_SIZE = 12;

@Component({
  selector: 'app-products-page',
  imports: [FormsModule, CurrencyPipe, DecimalPipe, Icon, ProductArt, ProductForm],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <section class="hero fade-up">
        <div>
          <span class="eyebrow"><app-icon name="sparkles" [size]="14" /> Curated catalog</span>
          <h1>Find something you'll love</h1>
          <p class="subtitle">Product reads are served cache-aside from Redis; any change by an admin or a paid order bumps the cache generation instantly.</p>
        </div>
        <div class="hero-meta">
          @if (cache(); as c) {
            <span class="badge" [class.badge-success]="c === 'HIT'" [class.badge-info]="c === 'MISS'" [title]="c === 'HIT' ? 'Response served from Redis' : 'Response served from PostgreSQL and cached'">
              <app-icon [name]="c === 'HIT' ? 'zap' : 'database'" [size]="13" />
              {{ c === 'HIT' ? 'Served from Redis cache' : 'Fetched from PostgreSQL' }}
            </span>
          }
          @if (auth.isAdmin()) {
            <button class="btn btn-primary" (click)="openEditor(null)"><app-icon name="plus" [size]="16" /> New product</button>
          }
        </div>
      </section>

      <section class="toolbar">
        <div class="input-icon grow search">
          <app-icon name="search" [size]="17" />
          <input class="input" type="search" placeholder="Search products…" [ngModel]="search()" (ngModelChange)="search$.next($event)" aria-label="Search products" />
        </div>
        <select class="select sort" [ngModel]="sort()" (ngModelChange)="setSort($event)" aria-label="Sort products">
          <option value="name">Name (A–Z)</option>
          <option value="price-asc">Price: low to high</option>
          <option value="price-desc">Price: high to low</option>
          <option value="rating">Top rated</option>
          <option value="newest">Newest</option>
        </select>
      </section>

      <section class="chips">
        <button class="chip" [class.active]="!category()" (click)="setCategory('')">All <span class="count">{{ totalAll() }}</span></button>
        @for (c of categories(); track c.name) {
          <button class="chip" [class.active]="category() === c.name" (click)="setCategory(c.name)">{{ c.name }} <span class="count">{{ c.count }}</span></button>
        }
      </section>

      @if (waking()) {
        <div class="alert alert-info waking-note">
          <span class="spinner"></span>
          <span>Waking up the catalog service. Free-tier hosting sleeps when idle, so this takes about 30 seconds. Products will appear automatically.</span>
        </div>
      }
      @if (failed() && products().length === 0) {
        <div class="card empty-state">
          <div class="empty-icon"><app-icon name="alert-circle" [size]="24" /></div>
          <h3>Couldn't load products</h3>
          <p class="small">The catalog service didn't respond. It may still be starting up.</p>
          <button class="btn btn-primary" (click)="retry()"><app-icon name="refresh" [size]="15" /> Try again</button>
        </div>
      } @else if (loading() && products().length === 0) {
        <div class="grid">
          @for (i of skeletons; track i) {
            <div class="card product"><div class="skeleton media"></div><div class="body stack"><div class="skeleton" style="height:14px;width:40%"></div><div class="skeleton" style="height:18px;width:80%"></div><div class="skeleton" style="height:36px"></div></div></div>
          }
        </div>
      } @else if (products().length === 0) {
        <div class="card empty-state">
          <div class="empty-icon"><app-icon name="search" [size]="24" /></div>
          <h3>No products found</h3>
          <p class="small">Try a different search term or category.</p>
          <button class="btn btn-soft" (click)="reset()">Clear filters</button>
        </div>
      } @else {
        <div class="grid" [class.dim]="loading()">
          @for (p of products(); track p.id; let i = $index) {
            <article class="card product fade-up" [style.animation-delay.ms]="i * 30">
              <div class="media">
                <app-product-art [name]="p.name" [category]="p.category" [imageUrl]="p.imageUrl" />
                @if (p.stock === 0) { <span class="stock-flag out">Sold out</span> }
                @else if (p.stock <= 5) { <span class="stock-flag low">Only {{ p.stock }} left</span> }
                @if (auth.isAdmin()) {
                  <div class="admin-actions">
                    <button class="btn btn-sm btn-icon" (click)="openEditor(p)" aria-label="Edit"><app-icon name="edit" [size]="14" /></button>
                    <button class="btn btn-sm btn-icon" (click)="confirmDelete.set(p)" aria-label="Delete"><app-icon name="trash" [size]="14" /></button>
                  </div>
                }
              </div>
              <div class="body">
                <div class="row-between">
                  <span class="xs subtle strong upper">{{ p.category }}</span>
                  <span class="rating"><app-icon name="star" [size]="13" /> {{ p.rating | number: '1.1-1' }}</span>
                </div>
                <h3 class="pname" [title]="p.name">{{ p.name }}</h3>
                <p class="desc">{{ p.description }}</p>
                <div class="row-between foot">
                  <span class="price">{{ p.price | currency }}</span>
                  <button class="btn btn-dark btn-sm" (click)="cart.add(p)" [disabled]="p.stock === 0">
                    <app-icon name="plus" [size]="14" /> Add
                  </button>
                </div>
              </div>
            </article>
          }
        </div>

        @if (totalPages() > 1) {
          <nav class="pager">
            <button class="btn btn-sm" (click)="goTo(page() - 1)" [disabled]="page() === 1"><app-icon name="chevron-left" [size]="14" /> Prev</button>
            <span class="small muted">Page {{ page() }} of {{ totalPages() }} · {{ total() }} products</span>
            <button class="btn btn-sm" (click)="goTo(page() + 1)" [disabled]="page() >= totalPages()">Next <app-icon name="chevron-right" [size]="14" /></button>
          </nav>
        }
      }
    </div>

    @if (editing() !== undefined) {
      <app-product-form [product]="editing() ?? null" [categories]="categoryNames()" (closed)="editing.set(undefined)" (saved)="onSaved($event)" />
    }

    @if (confirmDelete(); as p) {
      <div class="backdrop" (click)="confirmDelete.set(null)"></div>
      <div class="modal card-pad confirm">
        <div class="empty-icon danger"><app-icon name="trash" [size]="22" /></div>
        <h2>Delete "{{ p.name }}"?</h2>
        <p class="muted small">This removes the product from the catalog and invalidates the cache. Existing orders are unaffected.</p>
        <div class="row actions">
          <button class="btn btn-ghost" (click)="confirmDelete.set(null)">Cancel</button>
          <button class="btn btn-danger" (click)="delete(p)">Delete</button>
        </div>
      </div>
    }
  `,
  styles: `
    .waking-note { align-items: center; margin-bottom: 16px; }
    .hero { display: flex; align-items: flex-end; justify-content: space-between; gap: 20px; flex-wrap: wrap; margin-bottom: 24px; }
    .hero h1 { font-size: 2rem; }
    .hero .eyebrow { display: inline-flex; align-items: center; gap: 6px; font-size: 0.78rem; font-weight: 600; text-transform: uppercase; letter-spacing: 0.08em; color: var(--primary); margin-bottom: 8px; }
    .hero .subtitle { color: var(--text-2); margin-top: 8px; max-width: 620px; }
    .hero-meta { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
    .toolbar { display: flex; gap: 12px; margin-bottom: 14px; flex-wrap: wrap; }
    .search { min-width: 240px; }
    .sort { width: 210px; }
    .chips { display: flex; gap: 8px; flex-wrap: wrap; margin-bottom: 24px; }
    .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(250px, 1fr)); gap: 20px; transition: opacity 0.2s; }
    .grid.dim { opacity: 0.55; pointer-events: none; }
    .product { overflow: hidden; display: flex; flex-direction: column; transition: transform 0.2s var(--ease), box-shadow 0.2s; }
    .product:hover { transform: translateY(-3px); box-shadow: var(--shadow); }
    .media { position: relative; aspect-ratio: 4 / 3; overflow: hidden; }
    .stock-flag { position: absolute; left: 12px; top: 12px; font-size: 0.72rem; font-weight: 700; padding: 4px 9px; border-radius: 999px; backdrop-filter: blur(6px); }
    .stock-flag.low { background: rgba(255,251,235,0.92); color: var(--warning); }
    .stock-flag.out { background: rgba(15,23,42,0.8); color: #fff; }
    .admin-actions { position: absolute; right: 10px; top: 10px; display: flex; gap: 6px; opacity: 0; transition: opacity 0.15s; }
    .product:hover .admin-actions { opacity: 1; }
    .admin-actions .btn { background: rgba(255,255,255,0.95); }
    .body { padding: 16px; display: flex; flex-direction: column; gap: 6px; flex: 1; }
    .upper { text-transform: uppercase; letter-spacing: 0.06em; }
    .rating { display: inline-flex; align-items: center; gap: 4px; font-size: 0.8rem; font-weight: 600; color: #b45309; }
    .pname { font-size: 1rem; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .desc { color: var(--text-2); font-size: 0.84rem; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; min-height: 2.5em; }
    .foot { margin-top: auto; padding-top: 10px; }
    .price { font-size: 1.15rem; font-weight: 750; letter-spacing: -0.02em; }
    .pager { display: flex; align-items: center; justify-content: center; gap: 16px; margin-top: 32px; }
    .confirm { text-align: center; display: flex; flex-direction: column; align-items: center; gap: 10px; }
    .confirm .actions { margin-top: 10px; }
    .empty-icon.danger { width: 52px; height: 52px; border-radius: 16px; display: grid; place-items: center; background: var(--danger-50); color: var(--danger); }
    @media (max-width: 640px) { .sort { width: 100%; } .hero h1 { font-size: 1.6rem; } }
  `
})
export class ProductsPage implements OnInit {
  private readonly api = inject(CatalogApi);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly auth = inject(AuthService);
  protected readonly cart = inject(CartStore);

  protected readonly skeletons = Array.from({ length: 8 }, (_, i) => i);
  protected readonly search$ = new Subject<string>();

  protected readonly products = signal<Product[]>([]);
  protected readonly categories = signal<Category[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly search = signal('');
  protected readonly category = signal('');
  protected readonly sort = signal('name');
  protected readonly loading = signal(true);
  /** The first load is being retried because the catalog service is still waking up. */
  protected readonly waking = signal(false);
  protected readonly failed = signal(false);
  private loadSub?: Subscription;
  protected readonly cache = signal<'HIT' | 'MISS' | null>(null);

  protected readonly editing = signal<Product | null | undefined>(undefined);
  protected readonly confirmDelete = signal<Product | null>(null);

  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.total() / PAGE_SIZE)));
  protected readonly totalAll = computed(() => this.categories().reduce((n, c) => n + c.count, 0));
  protected readonly categoryNames = computed(() => this.categories().map(c => c.name));

  ngOnInit() {
    this.search$
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(term => {
        this.search.set(term);
        this.page.set(1);
        this.load();
      });
    this.loadCategories();
    this.load();
  }

  setCategory(category: string) {
    this.category.set(category);
    this.page.set(1);
    this.load();
  }

  setSort(sort: string) {
    this.sort.set(sort);
    this.page.set(1);
    this.load();
  }

  goTo(page: number) {
    this.page.set(page);
    this.load();
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  reset() {
    this.search.set('');
    this.category.set('');
    this.page.set(1);
    this.load();
  }

  openEditor(product: Product | null) {
    this.editing.set(product);
  }

  onSaved(product: Product) {
    this.toast.success('Product saved', `${product.name} — cache invalidated.`);
    this.editing.set(undefined);
    this.loadCategories();
    this.load();
  }

  delete(product: Product) {
    this.confirmDelete.set(null);
    this.api.delete(product.id).subscribe(() => {
      this.toast.success('Product deleted', product.name);
      this.loadCategories();
      this.load();
    });
  }

  retry() {
    this.loadCategories();
    this.load();
  }

  private load() {
    this.loading.set(true);
    this.failed.set(false);
    this.loadSub?.unsubscribe();
    this.loadSub = this.api
      .list({ search: this.search(), category: this.category(), sort: this.sort(), page: this.page(), pageSize: PAGE_SIZE })
      .pipe(
        retryWhileWaking(() => this.waking.set(true)),
        finalize(() => this.waking.set(false))
      )
      .subscribe({
        next: ({ data, cache }) => {
          this.products.set(data.items);
          this.total.set(data.totalCount);
          this.cache.set(cache);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.failed.set(true);
        }
      });
  }

  private loadCategories() {
    this.api.categories().pipe(retryWhileWaking()).subscribe({ next: c => this.categories.set(c), error: () => undefined });
  }
}
