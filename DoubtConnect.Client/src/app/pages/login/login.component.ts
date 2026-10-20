
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  FormBuilder,
  ReactiveFormsModule,
  Validators
} from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    RouterLink
  ],
  templateUrl: './login.component.html',
  styleUrl: './login.component.css'
})
export class LoginComponent {
  errorMessage = '';
  successMessage = '';
  isLoading = false;

  loginForm;

  constructor(
    private fb: FormBuilder,
    private authService: AuthService,
    private router: Router
  ) {
    this.loginForm = this.fb.nonNullable.group({
      email: ['', [
        Validators.required,
        Validators.email
      ]],
      password: ['', [
        Validators.required
      ]]
    });
  }

  onSubmit(): void {
    this.errorMessage = '';
    this.successMessage = '';

    if (this.loginForm.invalid) {
      this.loginForm.markAllAsTouched();
      return;
    }

    this.isLoading = true;

    const credentials = this.loginForm.getRawValue();

    this.authService.login({
      email: credentials.email.trim(),
      password: credentials.password
    }).subscribe({
      next: (response) => {
        this.isLoading = false;

        if (!response.accessToken || !response.user) {
          this.errorMessage =
            'The server returned an incomplete login response.';
          return;
        }

        const roles = response.user.roles ?? [];

        if (roles.includes('Mentor')) {
          sessionStorage.setItem(
            'accessToken',
            response.accessToken
          );
          sessionStorage.setItem(
            'user',
            JSON.stringify(response.user)
          );

          this.router.navigate(['/mentor-dashboard']);
        } else if (roles.includes('Student')) {
          sessionStorage.setItem(
            'accessToken',
            response.accessToken
          );
          sessionStorage.setItem(
            'user',
            JSON.stringify(response.user)
          );

          this.router.navigate(['/student-dashboard']);
        } else {
          sessionStorage.removeItem('accessToken');
          sessionStorage.removeItem('user');

          this.errorMessage =
            'Your account does not have a valid Student or Mentor role.';
        }
      },

      error: (error: HttpErrorResponse) => {
        this.isLoading = false;

        if (error.status === 0) {
          this.errorMessage =
            'Cannot connect to the server. Make sure the API is running.';
        } else if (error.status === 400) {
          this.errorMessage =
            'Please check your email and password.';
        } else if (error.status === 401) {
          this.errorMessage =
            'Invalid email or password. Please try again.';
        } else {
          this.errorMessage =
            error.error?.message ||
            'Something went wrong. Please try again.';
        }
      }
    });
  }
}
