import { Routes } from '@angular/router';
import { adminGuard, authGuard, guestGuard } from './core/auth/guards';
import { Shell } from './layout/shell';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    title: 'Sign in · OrderFlow',
    loadComponent: () => import('./features/auth/login.page').then(m => m.LoginPage)
  },
  {
    path: 'register',
    canActivate: [guestGuard],
    title: 'Create account · OrderFlow',
    loadComponent: () => import('./features/auth/register.page').then(m => m.RegisterPage)
  },
  {
    path: '',
    component: Shell,
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'products' },
      {
        path: 'products',
        title: 'Catalog · OrderFlow',
        loadComponent: () => import('./features/products/products.page').then(m => m.ProductsPage)
      },
      {
        path: 'checkout',
        canActivate: [authGuard],
        title: 'Checkout · OrderFlow',
        loadComponent: () => import('./features/checkout/checkout.page').then(m => m.CheckoutPage)
      },
      {
        path: 'orders',
        canActivate: [authGuard],
        title: 'Orders · OrderFlow',
        loadComponent: () => import('./features/orders/orders.page').then(m => m.OrdersPage)
      },
      {
        path: 'admin/logs',
        canActivate: [adminGuard],
        title: 'System monitor · OrderFlow',
        loadComponent: () => import('./features/admin/admin-logs.page').then(m => m.AdminLogsPage)
      }
    ]
  },
  { path: '**', redirectTo: 'products' }
];
