import { Routes } from '@angular/router';
import { Admin } from './pages/admin';
import { Home } from './pages/home';
import { ProductDetailPage } from './pages/product-detail';
import { Products } from './pages/products';

export const routes: Routes = [
  { path: '', component: Home, title: 'AutoPrice NZ' },
  { path: 'products', component: Products, title: 'Products — AutoPrice NZ' },
  { path: 'product/:id', component: ProductDetailPage, title: 'Product — AutoPrice NZ' },
  { path: 'admin', component: Admin, title: 'Admin — AutoPrice NZ' },
  { path: '**', redirectTo: '' },
];
