import { ChangeDetectionStrategy, Component, OnInit, inject, input, output, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { CatalogApi } from '../../core/api.service';
import { problemMessage } from '../../core/error.interceptor';
import { Product, UpsertProduct } from '../../core/models';
import { Icon } from '../../shared/icon';

@Component({
  selector: 'app-product-form',
  imports: [ReactiveFormsModule, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="backdrop" (click)="closed.emit()"></div>
    <div class="modal" role="dialog" [attr.aria-label]="product() ? 'Edit product' : 'New product'">
      <div class="card-header">
        <div>
          <h2>{{ product() ? 'Edit product' : 'New product' }}</h2>
          <p class="xs muted">Saving invalidates the Redis catalog cache instantly.</p>
        </div>
        <button class="btn btn-ghost btn-icon" (click)="closed.emit()" aria-label="Close"><app-icon name="x" /></button>
      </div>
      <form [formGroup]="form" (ngSubmit)="save()" class="card-pad" novalidate>
        <div class="form-grid">
          <div class="field span-2">
            <label for="pname">Name</label>
            <input id="pname" class="input" formControlName="name" />
          </div>
          <div class="field">
            <label for="pcat">Category</label>
            <input id="pcat" class="input" formControlName="category" list="categories" />
            <datalist id="categories">
              @for (c of categories(); track c) { <option [value]="c"></option> }
            </datalist>
          </div>
          <div class="field">
            <label for="prating">Rating (0–5)</label>
            <input id="prating" class="input" type="number" step="0.1" min="0" max="5" formControlName="rating" />
          </div>
          <div class="field">
            <label for="pprice">Price (USD)</label>
            <input id="pprice" class="input" type="number" step="0.01" min="0" formControlName="price" />
          </div>
          <div class="field">
            <label for="pstock">Stock</label>
            <input id="pstock" class="input" type="number" step="1" min="0" formControlName="stock" />
          </div>
          <div class="field span-2">
            <label for="pimg">Image URL <span class="subtle">(optional, https)</span></label>
            <input id="pimg" class="input" formControlName="imageUrl" placeholder="https://…" />
          </div>
          <div class="field span-2">
            <label for="pdesc">Description</label>
            <textarea id="pdesc" class="textarea" formControlName="description" rows="3"></textarea>
          </div>
        </div>

        @if (error()) { <div class="alert alert-danger mt"><app-icon name="alert-circle" /> {{ error() }}</div> }

        <div class="row actions">
          <button type="button" class="btn btn-ghost" (click)="closed.emit()">Cancel</button>
          <button type="submit" class="btn btn-primary" [disabled]="saving()">
            @if (saving()) { <span class="spinner"></span> } Save product
          </button>
        </div>
      </form>
    </div>
  `,
  styles: `
    .actions { justify-content: flex-end; margin-top: 20px; }
    .mt { margin-top: 16px; }
  `
})
export class ProductForm implements OnInit {
  private readonly api = inject(CatalogApi);

  readonly product = input<Product | null>(null);
  readonly categories = input<string[]>([]);
  readonly closed = output<void>();
  readonly saved = output<Product>();

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.maxLength(120)]],
    category: ['', [Validators.required, Validators.maxLength(60)]],
    price: [10, [Validators.required, Validators.min(0.01)]],
    stock: [10, [Validators.required, Validators.min(0)]],
    rating: [4.5, [Validators.min(0), Validators.max(5)]],
    imageUrl: [''],
    description: ['', [Validators.required, Validators.maxLength(1000)]]
  });

  ngOnInit() {
    const p = this.product();
    if (p) {
      this.form.setValue({
        name: p.name, category: p.category, price: p.price, stock: p.stock,
        rating: p.rating, imageUrl: p.imageUrl ?? '', description: p.description
      });
    }
  }

  save() {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.error.set('Please fill in all required fields.');
      return;
    }
    const v = this.form.getRawValue();
    const body: UpsertProduct = { ...v, imageUrl: v.imageUrl.trim() || null };
    this.saving.set(true);
    this.error.set(null);
    const p = this.product();
    (p ? this.api.update(p.id, body) : this.api.create(body)).subscribe({
      next: saved => this.saved.emit(saved),
      error: err => {
        this.error.set(problemMessage(err));
        this.saving.set(false);
      }
    });
  }
}
